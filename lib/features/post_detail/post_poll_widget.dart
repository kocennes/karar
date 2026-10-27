import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_pressable.dart';

/// Community poll (spec §6.1.10): 52 px muted pills; after voting each pill
/// shows a primaryContainer fill proportional to its share and the
/// percentage on the right; the chosen option gets a 2 px primary border
/// and a check.
class PostPollWidget extends StatelessWidget {
  const PostPollWidget({
    super.key,
    required this.poll,
    required this.onVote,
  });

  final PostPoll poll;
  final ValueChanged<String> onVote;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: Row(
            children: [
              Icon(Icons.poll_outlined, size: 20, color: kc.text2),
              const SizedBox(width: 8),
              Flexible(
                child: Semantics(
                  header: true,
                  child: Text(
                    'Topluluk Anketi',
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
        ),
        ...poll.options.map((option) => _PollOptionTile(
              option: option,
              totalVotes: poll.totalVotes,
              isSelected: poll.mySelectionId == option.id,
              hasVoted: poll.hasVoted,
              onTap: () => onVote(option.id),
            )),
        Padding(
          padding: const EdgeInsets.only(top: 2, left: 6),
          child: Text(
            '${poll.totalVotes} oy kullanıldı',
            style: AppTypography.meta.copyWith(
              color: kc.text3,
              fontFeatures: AppTypography.tabular,
            ),
          ),
        ),
      ],
    );
  }
}

class _PollOptionTile extends StatelessWidget {
  const _PollOptionTile({
    required this.option,
    required this.totalVotes,
    required this.isSelected,
    required this.hasVoted,
    required this.onTap,
  });

  final PollOption option;
  final int totalVotes;
  final bool isSelected;
  final bool hasVoted;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final percent = totalVotes == 0 ? 0.0 : option.voteCount / totalVotes;
    final percentLabel = '%${(percent * 100).round()}';
    final radius = KararRadius.all(26);
    // Share fill: brand tint over the muted pill. primaryContainer is only
    // 1.03:1 off muted in both themes, so the bar would be invisible.
    // Labels on it stay ≥ 4.5:1 (primaryText 4.64 / 4.52, text2 4.88 / 5.88).
    final shareFill = Color.alphaBlend(
      kc.primaryFill.withValues(alpha: kc.isDark ? 0.24 : 0.16),
      kc.muted,
    );

    final pill = Container(
      constraints: const BoxConstraints(minHeight: 52),
      decoration: BoxDecoration(
        color: kc.muted,
        borderRadius: radius,
        border: Border.all(
          color: isSelected ? kc.primary : kc.muted,
          width: 2,
        ),
      ),
      child: ClipRRect(
        borderRadius: radius,
        child: Stack(
          children: [
            if (hasVoted)
              Positioned.fill(
                child: Align(
                  alignment: AlignmentDirectional.centerStart,
                  child: TweenAnimationBuilder<double>(
                    tween: Tween(begin: 0, end: percent),
                    duration: KararMotion.d(context, KararMotion.split),
                    curve: KararMotion.standard,
                    builder: (context, value, _) => FractionallySizedBox(
                      widthFactor: value.clamp(0.0, 1.0),
                      heightFactor: 1,
                      child: ColoredBox(color: shareFill),
                    ),
                  ),
                ),
              ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
              child: Row(
                children: [
                  if (isSelected) ...[
                    Icon(
                      Icons.check_circle_rounded,
                      size: 20,
                      color: kc.primary,
                    ),
                    const SizedBox(width: 8),
                  ],
                  Expanded(
                    child: Text(
                      option.text,
                      style: AppTypography.bodyStrong.copyWith(
                        fontWeight:
                            isSelected ? FontWeight.w800 : FontWeight.w600,
                        color: isSelected ? kc.primaryText : kc.text1,
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  if (hasVoted)
                    Text(
                      percentLabel,
                      style: AppTypography.label.copyWith(
                        color: isSelected ? kc.primaryText : kc.text2,
                        fontFeatures: AppTypography.tabular,
                      ),
                    )
                  else
                    Icon(
                      Icons.radio_button_unchecked_rounded,
                      size: 22,
                      color: kc.outline,
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );

    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: KararPressable(
        onTap: hasVoted ? null : onTap,
        semanticLabel: option.text,
        semanticValue: hasVoted ? percentLabel : null,
        selected: isSelected,
        pressedScale: 0.98,
        focusRadius: 26,
        child: pill,
      ),
    );
  }
}
