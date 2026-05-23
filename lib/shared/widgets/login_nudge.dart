import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'karar_button.dart';

class LoginNudge extends StatelessWidget {
  const LoginNudge({
    super.key,
    required this.title,
    required this.message,
    this.returnTo,
    this.preferRegister = false,
    this.onLoginSuccess,
  });

  final String title;
  final String message;
  final String? returnTo;
  final bool preferRegister;
  final VoidCallback? onLoginSuccess;

  static Future<void> show(
    BuildContext context, {
    required String title,
    required String message,
    String? returnTo,
    bool preferRegister = false,
    VoidCallback? onLoginSuccess,
  }) {
    final currentLocation = GoRouterState.of(context).uri.toString();
    return showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (_) => LoginNudge(
        title: title,
        message: message,
        returnTo: returnTo ?? currentLocation,
        preferRegister: preferRegister,
        onLoginSuccess: onLoginSuccess,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(24, 0, 24, 32),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.account_circle_outlined,
              size: 64, color: Colors.grey),
          const SizedBox(height: 16),
          Text(
            title,
            style: Theme.of(context).textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.bold,
                ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 12),
          Text(
            message,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 32),
          if (preferRegister) ...[
            KararButton(
              label: 'Hesap Aç',
              onPressed: () {
                Navigator.pop(context);
                context.push(_authLocation('/auth/register'));
              },
            ),
            const SizedBox(height: 12),
            KararButton(
              label: 'Giriş Yap',
              variant: KararButtonVariant.outlined,
              onPressed: () {
                Navigator.pop(context);
                context.push(_authLocation('/auth/login'));
              },
            ),
          ] else ...[
            KararButton(
              label: 'Giriş Yap',
              onPressed: () {
                Navigator.pop(context);
                context.push(_authLocation('/auth/login'));
              },
            ),
            const SizedBox(height: 12),
            KararButton(
              label: 'Hesap Oluştur',
              variant: KararButtonVariant.outlined,
              onPressed: () {
                Navigator.pop(context);
                context.push(_authLocation('/auth/register'));
              },
            ),
          ],
          const SizedBox(height: 12),
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: Text(preferRegister ? 'Şimdilik hayır' : 'Şimdilik değil'),
          ),
        ],
      ),
    );
  }

  String _authLocation(String path) {
    final target = returnTo;
    if (target == null || target.isEmpty || target.startsWith('/auth/')) {
      return path;
    }
    return '$path?returnTo=${Uri.encodeQueryComponent(target)}';
  }
}
