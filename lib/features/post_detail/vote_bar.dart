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
    if (!post.showPercentage) {
      return Semantics(
        label: '${post.totalVotes} oy kullanıldı. Karar için 40 oy bekleniyor.',
        child: Container(
          height: isCompact ? 40 : 52,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: AppColors.surfaceVariant.withValues(alpha: 0.5),
            borderRadius: BorderRadius.circular(6),
            border: Border.all(
              color: Theme.of(context).dividerColor.withValues(alpha: 0.05),
            ),
          ),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12),
            child: Text(
              '${post.totalVotes} oy - Karar için 40 oy bekleniyor',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              textAlign: TextAlign.center,
              style: AppTypography.metaText.copyWith(
                color: AppColors.textSecondary,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ),
      );
    }

    final hakliPercent = post.hakliPercent;
    final haksizPercent = 100 - hakliPercent;

    return Semantics(
      label:
          'Oy dağılımı: yüzde $hakliPercent haklı, yüzde $haksizPercent haksız. Toplam ${post.totalVotes} oy.',
      child: ClipRRect(
        borderRadius: BorderRadius.circular(6),
        child: SizedBox(
          height: isCompact ? 40 : 52,
          child: LayoutBuilder(
            builder: (context, constraints) {
              final totalWidth = constraints.maxWidth;
              final hakliWidth =
                  (totalWidth * hakliPercent / 100).clamp(0.0, totalWidth);
              final haksizWidth = totalWidth - hakliWidth;
              final barFontSize = isCompact ? 13.0 : 15.0;

              return Row(
                children: [
                  if (hakliPercent > 0)
                    AnimatedContainer(
                      duration: const Duration(milliseconds: 350),
                      curve: Curves.easeInOut,
                      width: hakliWidth,
                      color: AppColors.hakli,
                      alignment: Alignment.centerLeft,
                      padding: const EdgeInsets.symmetric(horizontal: 12),
                      child: Row(
                        children: [
                          const Icon(
                            Icons.check_circle,
                            color: Colors.white,
                            size: 14,
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
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  if (haksizPercent > 0)
                    AnimatedContainer(
                      duration: const Duration(milliseconds: 350),
                      curve: Curves.easeInOut,
                      width: haksizWidth,
                      color: AppColors.haksiz,
                      alignment: Alignment.centerRight,
                      padding: const EdgeInsets.symmetric(horizontal: 12),
                      child: Row(
                        mainAxisAlignment: MainAxisAlignment.end,
                        children: [
                          Flexible(
                            child: Text(
                              'Haksız  %$haksizPercent',
                              maxLines: 1,
                              textAlign: TextAlign.right,
                              overflow: TextOverflow.ellipsis,
                              style: AppTypography.votePercent.copyWith(
                                color: Colors.white,
                                fontSize: barFontSize,
                              ),
                            ),
                          ),
                          const SizedBox(width: 6),
                          const Icon(Icons.cancel, color: Colors.white, size: 14),
                          const SizedBox(width: 2),
                        ],
                      ),
                    ),
                ],
              );
            },
          ),
        ),
      ),
    );
  }
}
