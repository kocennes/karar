import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/auth/auth_service.dart';
import '../../core/providers.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../../core/utils/date_formatter.dart';
import '../../shared/widgets/centered_content.dart';

class SessionsScreen extends ConsumerStatefulWidget {
  const SessionsScreen({super.key});

  @override
  ConsumerState<SessionsScreen> createState() => _SessionsScreenState();
}

class _SessionsScreenState extends ConsumerState<SessionsScreen> {
  List<UserSession>? _sessions;
  bool _isLoading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    setState(() => _isLoading = true);
    try {
      final sessions = await ref.read(authServiceProvider).fetchSessions();
      if (!mounted) return;
      setState(() {
        _sessions = sessions;
        _isLoading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = 'Oturumlar yüklenemedi.';
        _isLoading = false;
      });
    }
  }

  Future<void> _revoke(UserSession session) async {
    try {
      await ref.read(authServiceProvider).revokeSession(session.id);
      if (!mounted) return;
      setState(() {
        _sessions?.removeWhere((s) => s.id == session.id);
      });
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Oturum kapatıldı.')),
      );
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Oturum kapatılamadı.')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Aktif Oturumlar'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: _buildBody(),
      ),
    );
  }

  Widget _buildBody() {
    if (_isLoading) return const LoadingIndicator();
    if (_error != null) return ErrorView(message: _error!, onRetry: _load);
    if (_sessions == null || _sessions!.isEmpty) {
      return const Center(child: Text('Aktif oturum bulunamadı.'));
    }

    return ListView.separated(
      padding: const EdgeInsets.symmetric(vertical: 16),
      itemCount: _sessions!.length,
      separatorBuilder: (_, __) => const Divider(),
      itemBuilder: (context, index) {
        final session = _sessions![index];
        return ListTile(
          leading: Icon(
            session.platform.toLowerCase().contains('android')
                ? Icons.android
                : session.platform.toLowerCase().contains('ios')
                    ? Icons.apple
                    : Icons.devices,
          ),
          title: Text(session.platform),
          subtitle: Text(
            'Son görülme: ${DateFormatter.full(session.lastSeenAt)}',
            style: const TextStyle(fontSize: 12),
          ),
          trailing: session.isCurrent
              ? const Chip(
                  label: Text('Şu anki cihaz', style: TextStyle(fontSize: 10)),
                  visualDensity: VisualDensity.compact,
                )
              : IconButton(
                  icon: const Icon(Icons.logout, color: Colors.red),
                  onPressed: () => _confirmRevoke(session),
                  tooltip: 'Oturumu kapat',
                ),
        );
      },
    );
  }

  void _confirmRevoke(UserSession session) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Oturumu Kapat?'),
        content: const Text('Bu cihazdaki Karar oturumu sonlandırılacaktır.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Vazgeç')),
          FilledButton(
            onPressed: () {
              Navigator.pop(ctx);
              _revoke(session);
            },
            child: const Text('Oturumu Kapat'),
          ),
        ],
      ),
    );
  }
}

