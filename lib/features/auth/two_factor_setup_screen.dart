import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_exception.dart';
import '../../core/providers.dart';
import '../../shared/widgets/centered_content.dart';

class TwoFactorSetupScreen extends ConsumerStatefulWidget {
  const TwoFactorSetupScreen({super.key});

  @override
  ConsumerState<TwoFactorSetupScreen> createState() => _TwoFactorSetupScreenState();
}

class _TwoFactorSetupScreenState extends ConsumerState<TwoFactorSetupScreen> {
  String? _secret;
  String? _qrUrl;
  bool _isLoading = true;
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
        _secret = data['secretKey'] as String?;
        _qrUrl = data['qrCodeUrl'] as String?;
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
        _error = e.message;
        _isLoading = false;
      });
    } catch (_) {
      setState(() {
        _error = 'Doğrulama başarısız.';
        _isLoading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        leading: InkWell(
          onTap: () => context.go('/'),
          child: Padding(
            padding: const EdgeInsets.all(8.0),
            child: Image.asset('logo/logo.png', fit: BoxFit.contain),
          ),
        ),
        title: const Text('2FA Kurulumu'),
        centerTitle: true,
      ),
      body: CenteredContent(
        maxWidth: 400,
        child: _isLoading
            ? const Center(child: CircularProgressIndicator())
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
                    if (_qrUrl != null)
                      Center(
                        child: Container(
                          padding: const EdgeInsets.all(16),
                          decoration: BoxDecoration(
                            color: Colors.white,
                            borderRadius: BorderRadius.circular(12),
                          ),
                          child: Image.network(_qrUrl!, width: 200, height: 200),
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

