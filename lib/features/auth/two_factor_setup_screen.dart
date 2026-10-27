import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/api/api_exception.dart';
import '../../core/providers.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/content_unavailable_view.dart';
import '../../shared/widgets/karar_logo.dart';

class TwoFactorSetupScreen extends ConsumerStatefulWidget {
  const TwoFactorSetupScreen({super.key});

  @override
  ConsumerState<TwoFactorSetupScreen> createState() => _TwoFactorSetupScreenState();
}

class _TwoFactorSetupScreenState extends ConsumerState<TwoFactorSetupScreen> {
  String? _secret;
  String? _otpAuthUrl;
  bool _isLoading = true;
  bool _alreadyEnabled = false;
  String? _error;
  final _codeCtrl = TextEditingController();

  @override
  void initState() {
    super.initState();
    _fetchSetup();
  }

  @override
  void dispose() {
    _codeCtrl.dispose();
    super.dispose();
  }

  Future<void> _fetchSetup() async {
    setState(() => _isLoading = true);
    try {
      final data = await ref.read(authServiceProvider).setup2fa();
      setState(() {
        // API sözleşmesi: TwoFactorSetupResponse(secret, otpAuthUrl)
        _secret = data['secret'] as String?;
        _otpAuthUrl = data['otpAuthUrl'] as String?;
        _isLoading = false;
      });
    } on ApiException catch (e) {
      setState(() {
        _alreadyEnabled = e.code == 'TWO_FACTOR_ALREADY_ENABLED';
        _error = _alreadyEnabled ? e.message : 'Kurulum başlatılamadı.';
        _isLoading = false;
      });
    } catch (_) {
      setState(() {
        _error = 'Kurulum başlatılamadı.';
        _isLoading = false;
      });
    }
  }

  Future<void> _enable() async {
    final code = _codeCtrl.text.trim();
    if (code.length != 6) return;

    setState(() => _isLoading = true);
    try {
      final authService = ref.read(authServiceProvider);
      await authService.enable2fa(code);
      final codes = await authService.generateBackupCodes();
      if (!mounted) return;
      context.pushReplacement('/2fa/backup-codes', extra: codes);
    } on ApiException catch (e) {
      setState(() {
        _error = e.friendlyMessage;
        _isLoading = false;
      });
    } catch (_) {
      setState(() {
        _error = 'Doğrulama başarısız.';
        _isLoading = false;
      });
    }
  }

  Widget _buildStatus(BuildContext context) {
    if (_isLoading) return const Center(child: CircularProgressIndicator());
    return ContentUnavailableView(
      icon: Icons.verified_user_outlined,
      title: _error ?? 'İki faktörlü doğrulama zaten etkin.',
      message: 'Yeni bir doğrulama uygulaması kurmak için önce '
          "Ayarlar'dan iki faktörlü doğrulamayı kapat.",
      buttonLabel: 'Ayarlara dön',
      onPressed: () => context.go('/settings'),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        leadingWidth: 180,
        leading: InkWell(
          onTap: () => context.go('/'),
          child: const Padding(
            padding: EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: KararLogo(size: LogoSize.medium),
          ),
        ),
        title: const Text('2FA Kurulumu'),
        centerTitle: true,
      ),
      body: CenteredContent(
        maxWidth: 400,
        child: _isLoading || _alreadyEnabled
            ? _buildStatus(context)
            : SingleChildScrollView(
                padding: const EdgeInsets.all(24),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Text(
                      'Hesabınızı Google Authenticator gibi bir uygulama ile koruyun.',
                      style: TextStyle(fontWeight: FontWeight.bold),
                    ),
                    const SizedBox(height: 24),
                    // Sır içeren otpauth:// bağlantısı hiçbir zaman ağdan
                    // yüklenen bir görsele (üçüncü taraf QR servisi) verilmez.
                    if (_otpAuthUrl != null)
                      OutlinedButton.icon(
                        icon: const Icon(Icons.open_in_new),
                        label: const Text('Doğrulama uygulamasında aç'),
                        onPressed: () => launchUrl(
                          Uri.parse(_otpAuthUrl!),
                          mode: LaunchMode.externalApplication,
                        ),
                      ),
                    const SizedBox(height: 24),
                    if (_secret != null) ...[
                      const Text('Veya kodu manuel girin:', style: TextStyle(fontSize: 12)),
                      Row(
                        children: [
                          Expanded(
                            child: Text(
                              _secret!,
                              style: const TextStyle(
                                fontFamily: 'monospace',
                                fontWeight: FontWeight.bold,
                              ),
                            ),
                          ),
                          IconButton(
                            icon: const Icon(Icons.copy, size: 18),
                            onPressed: () {
                              Clipboard.setData(ClipboardData(text: _secret!));
                              ScaffoldMessenger.of(context).showSnackBar(
                                const SnackBar(content: Text('Kod kopyalandı.')),
                              );
                            },
                          ),
                        ],
                      ),
                    ],
                    const SizedBox(height: 32),
                    const Text('Uygulamadaki 6 haneli kodu girin:'),
                    const SizedBox(height: 12),
                    TextField(
                      controller: _codeCtrl,
                      keyboardType: TextInputType.number,
                      maxLength: 6,
                      decoration: InputDecoration(
                        hintText: '000000',
                        errorText: _error,
                        border: const OutlineInputBorder(),
                      ),
                    ),
                    const SizedBox(height: 24),
                    FilledButton(
                      onPressed: _enable,
                      style: FilledButton.styleFrom(
                        minimumSize: const Size.fromHeight(56),
                      ),
                      child: const Text('Doğrula ve Aktif Et'),
                    ),
                  ],
                ),
              ),
      ),
    );
  }
}

