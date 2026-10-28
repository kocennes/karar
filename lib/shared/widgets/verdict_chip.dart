import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../models/post.dart';

/// Where a [VerdictChip] sits.
enum VerdictChipTone {
  /// On cards/photos: light capsule + capsule shadow, theme-independent
  /// capsule text colours (#116B34 / #B4232A / #5B576D).
  onColor,

  /// On surfaces (compact cards): muted background, theme text tokens.
  onSurface,
}

/// One-line verdict read-out (spec §5.5): ✓ %p ▬▬▬▬▬ %q ✕, or the
/// "{n} oy · Karar için 40 oy bekleniyor" line below 40 votes. The side the
/// viewer chose sits in a filled pill. Bar fractions tween (`split`, 80 ms
/// delay) only when the counts change — no animation on first build.
class VerdictChip extends StatelessWidget {
  const VerdictChip({
    super.key,
    required this.post,
    this.tone = VerdictChipTone.onColor,
  });

  final Post post;
  final VerdictChipTone tone;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final onColor = tone == VerdictChipTone.onColor;
    final hakliText = onColor ? kc.onCapsuleHakli : kc.hakliText;
    final haksizText = onColor ? kc.onCapsuleHaksiz : kc.haksizText;
    final mutedText = onColor ? kc.onCapsuleMuted : kc.text2;
    final hakliIcon = onColor ? kc.hakliFill : kc.hakliText;
    final haksizIcon = onColor ? kc.haksizFill : kc.haksizText;

    final decoration = BoxDecoration(
      color: onColor ? kc.capsule : kc.muted,
      borderRadius: KararRadius.all(17),
      boxShadow: onColor ? KararShadow.capsule : null,
    );

    if (!post.showPercentage) {
      return Semantics(
        container: true,
        label:
            '${post.totalVotes} oy kullanıldı. Karar için 40 oy bekleniyor.',
        child: ExcludeSemantics(
          child: Container(
            constraints: const BoxConstraints(minHeight: 34),
            padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 7),
            decoration: decoration,
            child: Row(
              children: [
                Icon(Icons.how_to_vote_rounded, size: 16, color: mutedText),
                const SizedBox(width: 6),
                Expanded(
                  child: Text(
                    '${post.totalVotes} oy · Karar için 40 oy bekleniyor',
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: AppTypography.meta.copyWith(
                      fontWeight: FontWeight.w700,
                      color: mutedText,
                      letterSpacing: 0,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      );
    }

    final p = post.hakliPercent;
    final q = 100 - p;
    final mine = post.myVote;
    final percentStyle = AppTypography.labelS.copyWith(
      fontWeight: FontWeight.w800,
      fontFeatures: AppTypography.tabular,
    );

    return Semantics(
      container: true,
      label:
          'Oy dağılımı: yüzde $p haklı, yüzde $q haksız. Toplam ${post.totalVotes} oy.',
      child: ExcludeSemantics(
        child: Container(
          constraints: const BoxConstraints(minHeight: 34),
          padding: const EdgeInsets.symmetric(horizontal: 11, vertical: 5),
          decoration: decoration,
          child: Row(
            children: [
              Icon(Icons.check_rounded, size: 16, color: hakliIcon),
              const SizedBox(width: 6),
              _Percent(
                text: '%$p',
                style: percentStyle,
                color: hakliText,
                mine: mine == VoteType.hakli,
                fill: kc.hakliFill,
              ),
              const SizedBox(width: 8),
              Expanded(
                // On the light capsule the fills read; on dark surfaces the
                // dark-tuned bar tones match the bright labels beside them.
                child: _SplitBar(
                  fraction: p / 100,
                  hakli: onColor ? kc.hakliFill : kc.hakliBar,
                  haksiz: onColor ? kc.haksizFill : kc.haksizBar,
                ),
              ),
              const SizedBox(width: 8),
              _Percent(
                text: '%$q',
                style: percentStyle,
                color: haksizText,
                mine: mine == VoteType.haksiz,
                fill: kc.haksizFill,
              ),
              const SizedBox(width: 6),
              Icon(Icons.close_rounded, size: 16, color: haksizIcon),
            ],
          ),
        ),
      ),
    );
  }
}

/// A percentage; when it is the viewer's side it sits in a filled pill that
/// pops (1 → 1.12 → 1) when the choice is made (not on first build).
class _Percent extends StatefulWidget {
  const _Percent({
    required this.text,
    required this.style,
    required this.color,
    required this.mine,
    required this.fill,
  });

  final String text;
  final TextStyle style;
  final Color color;
  final bool mine;
  final Color fill;

  @override
  State<_Percent> createState() => _PercentState();
}

class _PercentState extends State<_Percent>
    with SingleTickerProviderStateMixin {
  // 80 ms delay + 600 ms pop, expressed as an Interval (no timers).
  static const _delay = Interval(80 / 680, 1);

  late final AnimationController _pop = AnimationController(
    vsync: this,
    duration: KararMotion.reveal + const Duration(milliseconds: 80),
    value: 1,
  );

  @override
  void didUpdateWidget(_Percent oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.mine && !oldWidget.mine && !KararMotion.reduce(context)) {
      _pop.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _pop.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final mine = widget.mine;
    final pill = AnimatedContainer(
      duration: KararMotion.d(context, KararMotion.base),
      curve: KararMotion.standard,
      padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
      decoration: BoxDecoration(
        color: mine ? widget.fill : widget.fill.withValues(alpha: 0),
        borderRadius: KararRadius.all(11),
      ),
      child: Text(
        widget.text,
        maxLines: 1,
        softWrap: false,
        style: widget.style.copyWith(
          color: mine ? context.kc.onColor : widget.color,
        ),
      ),
    );
    return AnimatedBuilder(
      animation: _pop,
      builder: (context, child) {
        final t = _delay.transform(_pop.value);
        // 0 → 0.4: 1 → 1.12 (spring), 0.4 → 1: back to 1.
        final scale = t >= 1
            ? 1.0
            : t < 0.4
                ? 1 + 0.12 * KararMotion.spring.transform(t / 0.4)
                : 1.12 -
                    0.12 * KararMotion.standard.transform((t - 0.4) / 0.6);
        return Transform.scale(scale: scale, child: child);
      },
      child: pill,
    );
  }
}

/// Hakli | 2 px gap | Haksız split bar (h 6, radius 3) that tweens from the
/// previous fraction when it changes (not on first build).
class _SplitBar extends StatefulWidget {
  const _SplitBar({
    required this.fraction,
    required this.hakli,
    required this.haksiz,
  });

  final double fraction;
  final Color hakli;
  final Color haksiz;

  @override
  State<_SplitBar> createState() => _SplitBarState();
}

class _SplitBarState extends State<_SplitBar>
    with SingleTickerProviderStateMixin {
  // 80 ms delay + `split` (420 ms), expressed as an Interval (no timers).
  static const _curve = Interval(80 / 500, 1, curve: Curves.easeOutCubic);

  late final AnimationController _c = AnimationController(
    vsync: this,
    duration: KararMotion.split + const Duration(milliseconds: 80),
    value: 1,
  );
  late double _from = widget.fraction;
  late double _to = widget.fraction;

  @override
  void didUpdateWidget(_SplitBar oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.fraction != widget.fraction) {
      _from = _current;
      _to = widget.fraction;
      if (KararMotion.reduce(context)) {
        _c.value = 1;
      } else {
        _c.forward(from: 0);
      }
    }
  }

  double get _current {
    final t = _curve.transform(_c.value.clamp(0.0, 1.0));
    return _from + (_to - _from) * t;
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 6,
      child: AnimatedBuilder(
        animation: _c,
        builder: (context, _) {
          final f = _current.clamp(0.0, 1.0);
          return LayoutBuilder(
            builder: (context, constraints) {
              final w = constraints.maxWidth;
              final showGap = f > 0 && f < 1;
              final usable = showGap ? (w - 2).clamp(0.0, w) : w;
              final left = usable * f;
              return Row(
                children: [
                  if (left > 0)
                    Container(
                      width: left,
                      decoration: BoxDecoration(
                        color: widget.hakli,
                        borderRadius: KararRadius.all(3),
                      ),
                    ),
                  if (showGap) const SizedBox(width: 2),
                  if (usable - left > 0)
                    Container(
                      width: usable - left,
                      decoration: BoxDecoration(
                        color: widget.haksiz,
                        borderRadius: KararRadius.all(3),
                      ),
                    ),
                ],
              );
            },
          );
        },
      ),
    );
  }
}
