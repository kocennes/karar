import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';
import '../../core/config/remote_config_service.dart';
import '../../core/providers.dart';
import '../../core/theme/app_colors.dart';

class WellbeingBanner extends ConsumerWidget {
  const WellbeingBanner({super.key, required this.content});

  final String content;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final bannerEnabled = ref.watch(remoteConfigProvider).getBool(RemoteConfigKeys.communityHealthBannerEnabled);
    if (!bannerEnabled) return const SizedBox.shrink();

    final lowerContent = content.toLowerCase();
    final isCrisis = lowerContent.contains('intihar') ||
                     lowerContent.contains('ölmek istiyorum') ||
                     lowerContent.contains('canıma kıy');

    if (!isCrisis) return const SizedBox.shrink();

    return Container(
      width: double.infinity,
      margin: const EdgeInsets.symmetric(vertical: 16),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.primary.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.primary.withValues(alpha: 0.3)),
      ),
      child: Column(
        children: [
          const Row(
            children: [
              Icon(Icons.info_outline, color: AppColors.primary),
              SizedBox(width: 12),
              Expanded(
                child: Text(
                  'Yalnız Değilsin',
                  style: TextStyle(
                    fontWeight: FontWeight.bold,
                    color: AppColors.primary,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          const Text(
            'Zor bir dönemden geçiyor olabilirsin. Kendini güvende hissetmiyorsan hemen 112 Acil Çağrı Merkezini ara ya da en yakın acil servise başvur.',
            style: TextStyle(fontSize: 13, height: 1.4),
          ),
          const SizedBox(height: 12),
          SizedBox(
            width: double.infinity,
            child: OutlinedButton.icon(
              onPressed: () => _callEmergency(context),
              icon: const Icon(Icons.phone),
              label: const Text('112 Acil Çağrı'),
              style: OutlinedButton.styleFrom(
                foregroundColor: AppColors.primary,
                side: const BorderSide(color: AppColors.primary),
              ),
            ),
          ),
        ],
      ),
    );
  }

  // 182 artık MHRS randevu hattı; Türkiye'de ulusal intihar hattı olmadığından 112'ye
  // yönlendiriyoruz. canLaunchUrl tel: için Android'de <queries> ister, bu yüzden
  // doğrudan deneyip başaramazsak numarayı gösteriyoruz.
  static Future<void> _callEmergency(BuildContext context) async {
    final messenger = ScaffoldMessenger.maybeOf(context);
    var launched = false;
    try {
      launched = await launchUrl(Uri(scheme: 'tel', path: '112'));
    } catch (_) {
      launched = false;
    }
    if (!launched) {
      messenger?.showSnackBar(
        const SnackBar(
          content: Text('Arama başlatılamadı. Lütfen telefonundan 112\'yi ara.'),
        ),
      );
    }
  }
}
