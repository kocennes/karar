import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/config/remote_config_service.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/widgets/karar_button.dart';

/// Crisis support banner (spec §6.1.6): warm warnSoft card, radius 20.
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

    final kc = context.kc;
    return Container(
      width: double.infinity,
      margin: const EdgeInsets.only(top: 16),
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: kc.warnSoft,
        borderRadius: KararRadius.all(KararRadius.tile),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(Icons.favorite_rounded, color: kc.voteIcon(false), size: 22),
              const SizedBox(width: 10),
              Expanded(
                child: Semantics(
                  header: true,
                  child: Text(
                    'Yalnız Değilsin',
                    style: AppTypography.bodyStrong.copyWith(
                      fontSize: 16,
                      fontWeight: FontWeight.w800,
                      color: kc.text1,
                    ),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            'Zor bir dönemden geçiyor olabilirsin. Kendini güvende hissetmiyorsan hemen 112 Acil Çağrı Merkezini ara ya da en yakın acil servise başvur.',
            style: AppTypography.bodyS.copyWith(color: kc.text2),
          ),
          const SizedBox(height: 14),
          KararButton(
            label: '112 Acil Çağrı',
            icon: Icons.phone_rounded,
            variant: KararButtonVariant.tonal,
            onPressed: () => _callEmergency(context),
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
