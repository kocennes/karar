import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import '../../core/theme/app_typography.dart';
import '../../shared/models/post.dart';

class VoteBar extends StatelessWidget {
  const VoteBar({
    super.key,
    required this.post,
    this.isCompact = false,
  });

  final Post post;
  final bool isCompact;

  @override
  Widget build(BuildContext context) {
    final height = isCompact ? 40.0 : 52.0;

    if (!post.showPercentage) {
      return Semantics(
        label: '${post.totalVotes} oy kullanıldı. Karar için 40 oy bekleniyor.',
        child: Container(
          height: height,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.surfaceContainerHighest.withValues(alpha: 0.5),
            borderRadius: BorderRadius.circular(10),
            border: Border.all(
              color: Theme.of(context).dividerColor.withValues(alpha: 0.08),
            ),
          ),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12),
            child: Text(
              '${post.totalVotes} oy · Karar için 40 oy bekleniyor',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              textAlign: TextAlign.center,
              style: AppTypography.metaText.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ),
      );
    }

    final hakliPercent = post.hakliPercent;
    final haksizPercent = 100 - hakliPercent;
    final barFontSize = isCompact ? 12.0 : 14.0;
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final hakliColor = isDark ? AppColors.darkHakli : AppColors.hakli;
    final haksizColor = isDark ? AppColors.darkHaksiz : AppColors.haksiz;

    return Semantics(
      label: 'Oy dağılımı: yüzde $hakliPercent haklı, yüzde $haksizPercent haksız. Toplam ${post.totalVotes} oy.',
      child: TweenAnimationBuilder<double>(
        tween: Tween(begin: 0, end: hakliPercent / 100),
        duration: const Duration(milliseconds: 1200),
        curve: Curves.easeOut,
        builder: (context, animValue, _) {
          return ClipRRect(
            borderRadius: BorderRadius.circular(10),
            child: SizedBox(
              height: height,
              child: Row(
                children: [
                  if (hakliPercent > 0)
                    Flexible(
                      flex: (animValue * 1000).round().clamp(1, 999),
                      child: Container(
                        color: hakliColor,
                        padding: const EdgeInsets.symmetric(horizontal: 10),
                        alignment: Alignment.centerLeft,
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Container(
                              width: barFontSize + 4,
                              height: barFontSize + 4,
                              decoration: BoxDecoration(
                                color: Colors.white.withValues(alpha: 0.25),
                                shape: BoxShape.circle,
                              ),
                              child: Icon(Icons.check, color: Colors.white, size: barFontSize - 2),
                            ),
                            const SizedBox(width: 6),
                            Flexible(
                              child: Text(
                                'Haklı  %$hakliPercent',
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: AppTypography.votePercent.copyWith(
                                  color: Colors.white,
                                  fontSize: barFontSize,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  if (haksizPercent > 0)
                    Flexible(
                      flex: ((1 - animValue) * 1000).round().clamp(1, 999),
                      child: Container(
                        color: haksizColor,
                        padding: const EdgeInsets.symmetric(horizontal: 10),
                        alignment: Alignment.centerRight,
                        child: Row(
                          mainAxisAlignment: MainAxisAlignment.end,
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Flexible(
                              child: Text(
                                'Haksız  %$haksizPercent',
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                textAlign: TextAlign.right,
                                style: AppTypography.votePercent.copyWith(
                                  color: Colors.white,
                                  fontSize: barFontSize,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                            ),
                            const SizedBox(width: 6),
                            Container(
                              width: barFontSize + 4,
                              height: barFontSize + 4,
                              decoration: BoxDecoration(
                                color: Colors.white.withValues(alpha: 0.25),
                                shape: BoxShape.circle,
                              ),
                              child: Icon(Icons.close, color: Colors.white, size: barFontSize - 2),
                            ),
                          ],
                        ),
                      ),
                    ),
                ],
              ),
            ),
          );
        },
      ),
    );
  }
}
