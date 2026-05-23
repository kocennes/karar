import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/theme/app_colors.dart';

class ContactScreen extends StatelessWidget {
  const ContactScreen({super.key});

  static const _entries = [
    _ContactEntry(
      icon: Icons.notifications_outlined,
      title: 'Bildirim Adresi',
      subtitle: '5651 sayılı Kanun kapsamında içerik bildirimleri',
      value: 'bildirim@karar.app',
      isEmail: true,
    ),
    _ContactEntry(
      icon: Icons.support_agent_outlined,
      title: 'Destek E-postası',
      subtitle: 'Genel sorular, hesap ve teknik destek',
      value: 'destek@karar.app',
      isEmail: true,
    ),
    _ContactEntry(
      icon: Icons.remove_circle_outline,
      title: 'İçerik Kaldırma',
      subtitle: 'Hukuka aykırı içerik kaldırma talepleri',
      value: 'icerik@karar.app',
      isEmail: true,
    ),
    _ContactEntry(
      icon: Icons.copyright_outlined,
      title: 'Telif Hakkı / FSEK',
      subtitle: 'Fikir ve Sanat Eserleri Kanunu kapsamında şikayetler',
      value: 'telif@karar.app',
      isEmail: true,
    ),
  ];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('İletişim ve Yer Sağlayıcı Bilgileri')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          _InfoBanner(),
          const SizedBox(height: 20),
          ..._entries.map((e) => _ContactCard(entry: e)),
          const SizedBox(height: 8),
          FilledButton.icon(
            onPressed: () => context.push('/legal/copyright'),
            icon: const Icon(Icons.copyright_outlined),
            label: const Text('Telif / FSEK Kaldırma Formu'),
          ),
          const SizedBox(height: 16),
          _LegalNote(),
        ],
      ),
    );
  }
}

class _InfoBanner extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.primary.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.primary.withValues(alpha: 0.2)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.info_outline, size: 20, color: AppColors.primary),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              'Karar, 5651 sayılı Kanun kapsamında yer sağlayıcı olarak faaliyet göstermektedir. '
              'Hukuka aykırı içerik bildirimleri ve kaldırma taleplerinizi aşağıdaki kanallar '
              'aracılığıyla iletebilirsiniz. Talepler en geç 48 saat içinde incelenir.',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: AppColors.primary,
                    height: 1.5,
                  ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ContactCard extends StatelessWidget {
  const _ContactCard({required this.entry});
  final _ContactEntry entry;

  Future<void> _openEmail(BuildContext context, String email) async {
    final uri = Uri(scheme: 'mailto', path: email);
    if (await canLaunchUrl(uri)) {
      await launchUrl(uri);
    } else {
      await Clipboard.setData(ClipboardData(text: email));
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('$email kopyalandı.')),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Card(
      margin: const EdgeInsets.only(bottom: 10),
      child: ListTile(
        leading: CircleAvatar(
          backgroundColor: AppColors.primary.withValues(alpha: 0.1),
          child: Icon(entry.icon, size: 20, color: AppColors.primary),
        ),
        title: Text(
          entry.title,
          style: const TextStyle(fontWeight: FontWeight.w600),
        ),
        subtitle: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const SizedBox(height: 2),
            Text(
              entry.subtitle,
              style:
                  const TextStyle(fontSize: 12, color: AppColors.textSecondary),
            ),
            const SizedBox(height: 4),
            Text(
              entry.value,
              style: TextStyle(
                color: AppColors.primary,
                fontWeight: FontWeight.w500,
                decoration: TextDecoration.underline,
                decorationColor: AppColors.primary.withValues(alpha: 0.5),
              ),
            ),
          ],
        ),
        isThreeLine: true,
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            IconButton(
              tooltip: 'Kopyala',
              icon: const Icon(Icons.copy_outlined, size: 18),
              onPressed: () async {
                await Clipboard.setData(ClipboardData(text: entry.value));
                if (context.mounted) {
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(content: Text('${entry.value} kopyalandı.')),
                  );
                }
              },
            ),
            if (entry.isEmail)
              IconButton(
                tooltip: 'E-posta gönder',
                icon: const Icon(Icons.open_in_new_outlined, size: 18),
                onPressed: () => _openEmail(context, entry.value),
              ),
          ],
        ),
        onTap: () => _openEmail(context, entry.value),
      ),
    );
  }
}

class _LegalNote extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Text(
      '5651 sayılı Kanun kapsamında yer sağlayıcı sıfatıyla bu bilgiler zorunlu olarak '
      'paylaşılmaktadır. Yanıt süresi talebin niteliğine göre değişir.',
      style: Theme.of(context).textTheme.bodySmall?.copyWith(
            color: AppColors.textTertiary,
            height: 1.6,
          ),
      textAlign: TextAlign.center,
    );
  }
}

class _ContactEntry {
  const _ContactEntry({
    required this.icon,
    required this.title,
    required this.subtitle,
    required this.value,
    this.isEmail = false,
  });

  final IconData icon;
  final String title;
  final String subtitle;
  final String value;
  final bool isEmail;
}
