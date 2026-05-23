import 'package:flutter/material.dart';

import 'notification_service.dart';

class NotificationPermissionDialog extends StatelessWidget {
  const NotificationPermissionDialog({
    super.key,
    required this.notificationService,
  });

  final NotificationService notificationService;

  // Shows the pre-dialog before the system permission prompt.
  // If user confirms, calls system permission; if not, marks as decided.
  static Future<void> showIfNeeded(
    BuildContext context, {
    required NotificationService notificationService,
    bool force = false,
  }) async {
    final alreadyDecided = await notificationService.isPermissionDecided();
    if (alreadyDecided) return;
    if (!context.mounted) return;

    await showDialog<void>(
      context: context,
      builder: (_) => NotificationPermissionDialog(
        notificationService: notificationService,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      icon: const Text('🔔', style: TextStyle(fontSize: 40)),
      title: const Text(
        'Haber Olmak İster misin?',
        textAlign: TextAlign.center,
      ),
      content: const Text(
        'Postun oylanınca veya topluluk karar verince seni haberdar edelim.',
        textAlign: TextAlign.center,
      ),
      actionsAlignment: MainAxisAlignment.center,
      actionsOverflowAlignment: OverflowBarAlignment.center,
      actions: [
        FilledButton(
          onPressed: () async {
            Navigator.pop(context);
            await notificationService.maybeRequestPermission(force: true);
          },
          child: const Text('Bildirimlere İzin Ver'),
        ),
        TextButton(
          onPressed: () async {
            Navigator.pop(context);
            await notificationService.markPermissionDecided();
          },
          child: const Text('Şimdilik değil'),
        ),
      ],
    );
  }
}
