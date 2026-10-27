import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/karar_pressable.dart';
import '../../shared/widgets/karar_surface.dart';
import 'vote_bar.dart';

/// Detail-screen verdict panel (spec §6.1.7): one read-out ([VoteBar]) and
/// one control (the two vote pills).
///
/// * Header: "Topluluk kararı" (+ "Oy Dağılımı" info at ≥ 100 votes, as
///   today) and the total vote count.
/// * ≥ 100 votes: the verdict word only ("Haklı" / "Haksız"); the
///   percentages live in the bar labels so they are shown once.
/// * Closed posts: a muted "Oylama kapandı" row and 40 % opacity buttons.
///
/// The vote logic stays with the caller: [onVote] receives the tapped side
/// (null disables both buttons). No UI haptic here — the detail notifier
/// already vibrates on native.
class VerdictPanel extends StatelessWidget {
  const VerdictPanel({
    super.key,
    required this.post,
    required this.onVote,
  });

  final Post post;

  /// Called with the tapped side; null when voting is closed.
  final ValueChanged<VoteType>? onVote;

  static const _verdictThreshold = 100;

  void _showVoteDetails(BuildContext context) {
    showDialog<void>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Oy Dağılımı'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            _VoteDetailRow(
              hakli: true,
              label: 'Haklı',
              count: post.voteCountHakli,
              percent: post.hakliPercent,
            ),
            const SizedBox(height: 16),
            _VoteDetailRow(
              hakli: false,
              label: 'Haksız',
              count: post.voteCountHaksiz,
              percent: 100 - post.hakliPercent,
            ),
            const SizedBox(height: 24),
            Text(
              'Toplam ${post.totalVotes} oy kullanıldı.',
              style: AppTypography.bodyS.copyWith(
                color: context.kc.text2,
                fontFeatures: AppTypography.tabular,
              ),
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Kapat'),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final showVerdict = post.totalVotes >= _verdictThreshold;
    final hakliWins = post.hakliPercent >= 50;

    return KararSurface(
      radius: KararRadius.panel,
      padding: const EdgeInsets.all(18),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 32),
            child: Row(
              children: [
                Expanded(
                  child: Row(
                    children: [
                      Flexible(
                        child: Text(
                          'Topluluk kararı',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style:
                              AppTypography.labelS.copyWith(color: kc.text2),
                        ),
                      ),
                      if (showVerdict) ...[
                        const SizedBox(width: 2),
                        KararCircleButton(
                          icon: Icons.info_outline_rounded,
                          onPressed: () => _showVoteDetails(context),
                          tooltip: 'Oy Dağılımı',
                          style: KararCircleStyle.muted,
                          size: 32,
                          iconSize: 16,
                          iconColor: kc.text2,
                        ),
                      ],
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                Text(
                  '${post.totalVotes} oy',
                  style: AppTypography.labelS.copyWith(
                    fontWeight: FontWeight.w800,
                    color: kc.primaryText,
                    fontFeatures: AppTypography.tabular,
                  ),
                ),
              ],
            ),
          ),
          if (showVerdict) ...[
            const SizedBox(height: 2),
            AnimatedSwitcher(
              duration: KararMotion.fade(context, KararMotion.base),
              switchInCurve: KararMotion.standard,
              switchOutCurve: KararMotion.exit,
              layoutBuilder: (current, previous) => Stack(
                alignment: Alignment.centerLeft,
                children: [...previous, if (current != null) current],
              ),
              child: Text(
                hakliWins ? 'Haklı' : 'Haksız',
                key: ValueKey(hakliWins),
                style: AppTypography.display.copyWith(
                  color: kc.voteText(hakliWins),
                ),
              ),
            ),
            const SizedBox(height: 14),
          ] else
            const SizedBox(height: 12),
          VoteBar(post: post),
          if (post.isClosed) ...[
            const SizedBox(height: 16),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
              decoration: BoxDecoration(
                color: kc.muted,
                borderRadius: KararRadius.all(KararRadius.input),
              ),
              child: Row(
                children: [
                  Icon(
                    Icons.lock_outline_rounded,
                    size: 16,
                    color: kc.text2,
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      'Oylama kapandı — 7 gün geçti',
                      style: AppTypography.labelS.copyWith(
                        fontWeight: FontWeight.w600,
                        color: kc.text2,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
          const SizedBox(height: 16),
          Row(
            children: [
              Expanded(
                child: _VotePill(
                  hakli: true,
                  selected: post.myVote == VoteType.hakli,
                  otherSelected: post.myVote == VoteType.haksiz,
                  onTap: onVote == null ? null : () => onVote!(VoteType.hakli),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: _VotePill(
                  hakli: false,
                  selected: post.myVote == VoteType.haksiz,
                  otherSelected: post.myVote == VoteType.hakli,
                  onTap:
                      onVote == null ? null : () => onVote!(VoteType.haksiz),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// One 56 px vote pill.
///
/// * nothing chosen: tonal (soft vote tint, vote text colour, outlined icon)
/// * chosen: solid vote fill, white label, filled icon, coloured glow (light)
/// * the other side chosen: transparent with a 1.5 px vote-colour outline
/// * disabled (closed): 40 % opacity
class _VotePill extends StatelessWidget {
  const _VotePill({
    required this.hakli,
    required this.selected,
    required this.otherSelected,
    required this.onTap,
  });

  final bool hakli;
  final bool selected;
  final bool otherSelected;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final label = hakli ? 'Haklı' : 'Haksız';
    final fill = kc.voteFill(hakli);
    final text = kc.voteText(hakli);
    final isLight = !kc.isDark;
    final duration = KararMotion.d(context, KararMotion.base);

    final Color bg;
    final Color fg;
    if (selected) {
      bg = fill;
      fg = Colors.white;
    } else if (otherSelected) {
      bg = fill.withValues(alpha: 0);
      fg = text;
    } else {
      bg = kc.voteSoft(hakli);
      fg = text;
    }

    final icon = hakli
        ? (selected ? Icons.check_circle_rounded : Icons.check_circle_outline_rounded)
        : (selected ? Icons.cancel_rounded : Icons.cancel_outlined);

    Widget pill = AnimatedContainer(
      duration: duration,
      curve: KararMotion.standard,
      constraints: const BoxConstraints(minHeight: 56),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: KararRadius.all(28),
        border: Border.all(
          color: otherSelected
              ? fill.withValues(alpha: 0.45)
              : fill.withValues(alpha: 0),
          width: 1.5,
        ),
        boxShadow: selected && isLight
            ? [
                BoxShadow(
                  color: fill.withValues(alpha: 0.45),
                  offset: const Offset(0, 10),
                  blurRadius: 22,
                  spreadRadius: -8,
                ),
              ]
            : [
                BoxShadow(
                  color: fill.withValues(alpha: 0),
                  offset: const Offset(0, 10),
                  blurRadius: 22,
                  spreadRadius: -8,
                ),
              ],
      ),
      alignment: Alignment.center,
      child: FittedBox(
        fit: BoxFit.scaleDown,
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            AnimatedSwitcher(
              duration: duration,
              transitionBuilder: (child, animation) => ScaleTransition(
                scale: animation.drive(CurveTween(curve: KararMotion.spring)),
                child: child,
              ),
              child: Icon(
                icon,
                key: ValueKey(icon),
                size: 22,
                color: fg,
              ),
            ),
            const SizedBox(width: 10),
            AnimatedDefaultTextStyle(
              duration: duration,
              style: AppTypography.buttonL.copyWith(color: fg),
              child: Text(label, maxLines: 1, softWrap: false),
            ),
          ],
        ),
      ),
    );

    if (onTap == null) pill = Opacity(opacity: 0.4, child: pill);

    return KararPressable(
      onTap: onTap,
      semanticLabel: selected ? '$label oyunu kaldır' : '$label oyu ver',
      selected: selected,
      pressedScale: 0.97,
      focusRadius: 28,
      child: pill,
    );
  }
}

class _VoteDetailRow extends StatelessWidget {
  const _VoteDetailRow({
    required this.hakli,
    required this.label,
    required this.count,
    required this.percent,
  });

  final bool hakli;
  final String label;
  final int count;
  final int percent;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              label,
              style: AppTypography.bodyStrong.copyWith(color: kc.text1),
            ),
            const SizedBox(width: 12),
            Flexible(
              child: Text(
                '$count oy (%$percent)',
                textAlign: TextAlign.end,
                style: AppTypography.bodyStrong.copyWith(
                  color: kc.voteText(hakli),
                  fontFeatures: AppTypography.tabular,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 8),
        ClipRRect(
          borderRadius: KararRadius.all(6),
          child: LinearProgressIndicator(
            value: percent / 100,
            color: kc.voteFill(hakli),
            backgroundColor: kc.voteSoft(hakli),
            minHeight: 12,
          ),
        ),
      ],
    );
  }
}
