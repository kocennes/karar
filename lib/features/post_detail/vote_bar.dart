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
            color: Theme.of(context)
                .colorScheme
                .surfaceContainerHighest
                .withValues(alpha: 0.5),
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
      label:
          'Oy dağılımı: yüzde $hakliPercent haklı, yüzde $haksizPercent haksız. Toplam ${post.totalVotes} oy.',
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
                      child: _VoteSegment(
                        color: hakliColor,
                        label: 'Haklı  %$hakliPercent',
                        icon: Icons.check,
                        alignEnd: false,
                        fontSize: barFontSize,
                      ),
                    ),
                  if (haksizPercent > 0)
                    Flexible(
                      flex: ((1 - animValue) * 1000).round().clamp(1, 999),
                      child: _VoteSegment(
                        color: haksizColor,
                        label: 'Haksız  %$haksizPercent',
                        icon: Icons.close,
                        alignEnd: true,
                        fontSize: barFontSize,
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

class _VoteSegment extends StatelessWidget {
  const _VoteSegment({
    required this.color,
    required this.label,
    required this.icon,
    required this.alignEnd,
    required this.fontSize,
  });

  final Color color;
  final String label;
  final IconData icon;
  final bool alignEnd;
  final double fontSize;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final hasRoomForIcon = constraints.maxWidth >= 56;
        final hasRoomForText = constraints.maxWidth >= 88;

        return Container(
          color: color,
          padding: EdgeInsets.symmetric(horizontal: hasRoomForText ? 10 : 0),
          alignment: alignEnd ? Alignment.centerRight : Alignment.centerLeft,
          child: hasRoomForIcon
              ? Row(
                  mainAxisAlignment: alignEnd
                      ? MainAxisAlignment.end
                      : MainAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: alignEnd
                      ? [
                          if (hasRoomForText) _buildLabel(TextAlign.right),
                          if (hasRoomForText) const SizedBox(width: 6),
                          _buildIcon(),
                        ]
                      : [
                          _buildIcon(),
                          if (hasRoomForText) const SizedBox(width: 6),
                          if (hasRoomForText) _buildLabel(TextAlign.left),
                        ],
                )
              : SizedBox.shrink(child: _buildHiddenLabel()),
        );
      },
    );
  }

  Widget _buildIcon() {
    return Container(
      width: fontSize + 4,
      height: fontSize + 4,
      decoration: BoxDecoration(
        color: Colors.white.withValues(alpha: 0.25),
        shape: BoxShape.circle,
      ),
      child: Icon(icon, color: Colors.white, size: fontSize - 2),
    );
  }

  Widget _buildLabel(TextAlign textAlign) {
    return Flexible(
      child: Text(
        label,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        textAlign: textAlign,
        style: AppTypography.votePercent.copyWith(
          color: Colors.white,
          fontSize: fontSize,
          fontWeight: FontWeight.w700,
        ),
      ),
    );
  }

  Widget _buildHiddenLabel() {
    return Text(
      label,
      maxLines: 1,
      overflow: TextOverflow.clip,
      style: AppTypography.votePercent.copyWith(
        color: Colors.transparent,
        fontSize: 1,
      ),
    );
  }
}
