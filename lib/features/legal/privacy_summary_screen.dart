import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../shared/widgets/centered_content.dart';

class PrivacySummaryScreen extends StatelessWidget {
  const PrivacySummaryScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Gizlilik Özeti'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 20, 16, 32),
          children: [
            _intro(context),
            const SizedBox(height: 20),
            _SummarySection(
              icon: Icons.inventory_2_outlined,
              title: 'Ne topluyoruz?',
              items: const [
                _Item('Cihaz kimliği',
                    'Anonim kullanıcı tanımak ve sahte hesapları önlemek için.'),
                _Item('IP adresi', 'Güvenlik ve hız limiti için kısa süre saklıyoruz.'),
                _Item('Kullanıcı adı ve e-posta',
                    'Hesap açmak istersen; tamamen isteğe bağlı.'),
                _Item('Postlar ve yorumlar', 'Platformun çalışması için.'),
                _Item('Bildirim tokeni (FCM)',
                    'Push bildirimi gönderebilmek için; izin verirsen.'),
                _Item('Anonim kullanım verisi',
                    'Uygulamayı geliştirmek için (kapatabilirsin).'),
              ],
            ),
            const SizedBox(height: 16),
            _SummarySection(
              icon: Icons.help_outline,
              title: 'Neden topluyoruz?',
              items: const [
                _Item('Platformun çalışması',
                    'Oy verme, yorum yapma ve içerik görüntüleme.'),
                _Item('Güvenlik', 'Sahte hesapları ve manipülasyonu engellemek.'),
                _Item('Moderasyon',
                    'Zararlı içerikleri tespit etmek (Perspective API kullanıyoruz).'),
                _Item('Bildirimler', 'Postuna oy geldiğinde haberdar etmek.'),
                _Item('Reklamlar',
                    'Google AdMob banner göstermek (rıza verirsen kişiselleştirilmiş).'),
              ],
            ),
            const SizedBox(height: 16),
            _SummarySection(
              icon: Icons.schedule_outlined,
              title: 'Ne kadar süre saklıyoruz?',
              items: const [
                _Item('Silinen postlar / yorumlar', '90 gün sonra kalıcı olarak silinir.'),
                _Item('Hesap bilgilerin (kullanıcı adı, e-posta)',
                    'Silme talebinden 30 gün sonra anonimleştirilir.'),
                _Item('IP kayıtları', '90 gün.'),
                _Item('Raporlar ve uyum kayıtları',
                    '1–2 yıl (5651 sayılı kanun gereği).'),
              ],
            ),
            const SizedBox(height: 24),
            _kvkkNote(context),
          ],
        ),
      ),
    );
  }

  Widget _intro(BuildContext context) {
    return Text(
      'Karar hangi verileri topladığını ve neden topladığını sana kısaca açıklamak istiyor. '
      'Hukuki metin okumak istersen Ayarlar → Gizlilik Politikası sayfasına bakabilirsin.',
      style: Theme.of(context).textTheme.bodyMedium?.copyWith(
            color: Theme.of(context).colorScheme.onSurfaceVariant,
          ),
    );
  }

  Widget _kvkkNote(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.verified_user_outlined,
                  size: 18, color: colorScheme.primary),
              const SizedBox(width: 8),
              Text(
                'KVKK Hakların',
                style: Theme.of(context).textTheme.labelLarge?.copyWith(
                      color: colorScheme.primary,
                      fontWeight: FontWeight.bold,
                    ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            'Verilerine erişme, düzeltme, silme ve aktarımdan haberdar olma hakkına sahipsin. '
            'Taleplerin 30 gün içinde yanıtlanır.',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: colorScheme.onSurfaceVariant,
                ),
          ),
          const SizedBox(height: 8),
          GestureDetector(
            onTap: () async {
              final uri = Uri(scheme: 'mailto', path: 'kvkk@karar.app');
              if (await canLaunchUrl(uri)) await launchUrl(uri);
            },
            child: Text(
              'kvkk@karar.app',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: colorScheme.primary,
                    decoration: TextDecoration.underline,
                  ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Item {
  const _Item(this.label, this.description);
  final String label;
  final String description;
}

class _SummarySection extends StatelessWidget {
  const _SummarySection({
    required this.icon,
    required this.title,
    required this.items,
  });

  final IconData icon;
  final String title;
  final List<_Item> items;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(icon, size: 20, color: colorScheme.primary),
            const SizedBox(width: 8),
            Text(
              title,
              style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
            ),
          ],
        ),
        const SizedBox(height: 10),
        ...items.map((item) => _ItemRow(item: item)),
      ],
    );
  }
}

class _ItemRow extends StatelessWidget {
  const _ItemRow({required this.item});
  final _Item item;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Icon(Icons.circle, size: 6, color: colorScheme.primary),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: RichText(
              text: TextSpan(
                style: Theme.of(context).textTheme.bodyMedium,
                children: [
                  TextSpan(
                    text: '${item.label}: ',
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                  TextSpan(
                    text: item.description,
                    style: TextStyle(
                        color: Theme.of(context).colorScheme.onSurfaceVariant),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
