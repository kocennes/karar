import 'dart:io';

import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import 'force_update_service.dart';

class ForceUpdateDialog extends StatelessWidget {
  const ForceUpdateDialog({super.key, required this.versionInfo});

  final VersionInfo versionInfo;

  static Future<void> showIfNeeded(
    BuildContext context,
    VersionInfo? info,
  ) async {
    if (info == null) return;
    if (!context.mounted) return;
    await showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (_) => ForceUpdateDialog(versionInfo: info),
    );
  }

  Future<void> _openStore() async {
    final url = Platform.isIOS
        ? versionInfo.iosStoreUrl
        : versionInfo.androidStoreUrl;
    final uri = Uri.parse(url);
    if (await canLaunchUrl(uri)) {
      await launchUrl(uri, mode: LaunchMode.externalApplication);
    }
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: false,
      child: AlertDialog(
        icon: const Text('⬆️', style: TextStyle(fontSize: 40)),
        title: const Text(
          'Güncelleme Gerekli',
          textAlign: TextAlign.center,
        ),
        content: const Text(
          'Uygulamanın daha iyi çalışması için güncelleme yapman gerekiyor.',
          textAlign: TextAlign.center,
        ),
        actionsAlignment: MainAxisAlignment.center,
        actions: [
          FilledButton(
            onPressed: _openStore,
            child: const Text('Güncelle'),
          ),
        ],
      ),
    );
  }
}
