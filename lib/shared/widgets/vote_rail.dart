import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../core/utils/count_format.dart';
import '../models/post.dart';
import 'glass_rail.dart';
import 'karar_pressable.dart';

/// Rail sizes (spec §5.5).
enum VoteRailSize { card, compact, fullscreen }

/// Geometry for each [VoteRailSize].
@immutable
class VoteRailMetrics {
  const VoteRailMetrics._({
    required this.width,
    required this.height,
    required this.voteCapsule,
    required this.commentCapsule,
    required this.circle,
    required this.gap,
    required this.dividerBlock,
  });

  final double width;
  final double height;
  final Size voteCapsule;
  final Size commentCapsule;
  final double circle;
  final double gap;
  final double dividerBlock;

  static const card = VoteRailMetrics._(
    width: 68,
    height: 384,
    voteCapsule: Size(52, 64),
    commentCapsule: Size(46, 58),
    circle: 46,
    gap: 8,
    dividerBlock: 14,
  );
  static const compact = VoteRailMetrics._(
    width: 64,
    height: 358,
    voteCapsule: Size(48, 58),
    commentCapsule: Size(44, 52),
    circle: 42,
    gap: 8,
    dividerBlock: 14,
  );
  static const fullscreen = VoteRailMetrics._(
    width: 72,
    height: 410,
    voteCapsule: Size(56, 68),
    commentCapsule: Size(50, 62),
    circle: 50,
    gap: 10,
    dividerBlock: 16,
  );

  static VoteRailMetrics of(VoteRailSize size) => switch (size) {
        VoteRailSize.card => card,
        VoteRailSize.compact => compact,
        VoteRailSize.fullscreen => fullscreen,
      };
}

/// The glass vote rail on post cards and Keşfet pages (spec §5.5 / §5.6).
///
/// Items top→bottom: Haklı capsule, Haksız capsule, divider, Yorum capsule,
/// Kaydet circle (if [onSave]), Paylaş circle (if [onShare]). Capsules
/// follow the theme: white + capsule shadow in light, dark tonal
/// (#24202F @ 92 %) + white 8 % hairline in dark, with the matching
/// `onCapsule*` content tones (text ≥ 4.5:1, icons ≥ 3:1 over any photo).
/// A selected vote capsule is filled with the vote colour (white content)
/// and ringed in `capsuleSelectedRing` (white / #F4F3F8 in dark).
/// Kaydet / Paylaş are glass circles (black 22 % on gradients, 28 % on
/// photos, white 28 % stroke, white icon) as in the reference, so only the
/// vote and comment capsules are solid; a saved post shows a solid capsule
/// with the brand bookmark.
///
/// The rail does not change data itself: it calls [onVote] and re-renders
/// whatever the provider state says (optimistic in feed, server-confirmed in
/// Keşfet via [pendingVote], rolled back on error).
class VoteRail extends StatefulWidget {
  const VoteRail({
    super.key,
    required this.hakliCount,
    required this.haksizCount,
    required this.commentCount,
    required this.myVote,
    required this.onVote,
    this.onComment,
    this.onSave,
    this.isSaved = false,
    this.onShare,
    this.pendingVote,
    this.size = VoteRailSize.card,
    this.liveBlur = false,
    this.pulseHakli = false,
    this.unvoteLabels = false,
    this.hakliKey = const ValueKey('feed_vote_hakli_button'),
    this.haksizKey = const ValueKey('feed_vote_haksiz_button'),
  });

  final int hakliCount;
  final int haksizCount;
  final int commentCount;
  final VoteType? myVote;

  /// Null = voting disabled (closed post): capsules at 60 % opacity.
  final ValueChanged<VoteType>? onVote;
  final VoidCallback? onComment;
  final VoidCallback? onSave;
  final bool isSaved;
  final VoidCallback? onShare;

  /// Shows a progress ring on that capsule; both vote capsules ignore taps
  /// while non-null.
  final VoteType? pendingVote;
  final VoteRailSize size;

  /// Live backdrop blur (photo cards / Keşfet photo pages only).
  final bool liveBlur;

  /// Repeating pulse ring on the Haklı capsule (first-vote coachmark).
  final bool pulseHakli;

  /// When true, a selected capsule is announced as "… oyunu kaldır"
  /// (screens where tapping the selected side removes the vote).
  final bool unvoteLabels;

  final Key? hakliKey;
  final Key? haksizKey;

  @override
  State<VoteRail> createState() => _VoteRailState();
}

class _VoteRailState extends State<VoteRail> with TickerProviderStateMixin {
  late final AnimationController _burst;
  late final AnimationController _coach;
  VoteType? _burstSide;

  @override
  void initState() {
    super.initState();
    _burst = AnimationController(vsync: this, duration: KararMotion.split);
    _coach = AnimationController(vsync: this, duration: KararMotion.pulse);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _syncCoach();
  }

  @override
  void didUpdateWidget(VoteRail oldWidget) {
    super.didUpdateWidget(oldWidget);
    final vote = widget.myVote;
    if (vote != null && vote != oldWidget.myVote && !KararMotion.reduce(context)) {
      _burstSide = vote;
      _burst.forward(from: 0);
    }
    _syncCoach();
  }

  bool _coachStarted = false;

  /// The coachmark pulse repeats a bounded number of times (≈ 6 s) so it
  /// never runs forever (battery, `pumpAndSettle`).
  static const _coachPulses = 5;

  void _syncCoach() {
    final run = widget.pulseHakli && !KararMotion.reduce(context);
    if (run && !_coachStarted) {
      _coachStarted = true;
      _coach.repeat(count: _coachPulses);
    } else if (!run && _coachStarted) {
      _coachStarted = false;
      _coach.stop();
      _coach.value = 0;
    }
  }

  @override
  void dispose() {
    _burst.dispose();
    _coach.dispose();
    super.dispose();
  }

  void _tapVote(VoteType type) {
    final onVote = widget.onVote;
    if (onVote == null || widget.pendingVote != null) return;
    if (widget.myVote == type) {
      KararHaptics.unvote();
    } else {
      KararHaptics.vote();
    }
    onVote(type);
  }

  @override
  Widget build(BuildContext context) {
    final m = VoteRailMetrics.of(widget.size);
    final kc = context.kc;
    final votingEnabled = widget.onVote != null;
    final pending = widget.pendingVote != null;

    Widget voteCapsule(VoteType type) {
      final hakli = type == VoteType.hakli;
      final selected = widget.myVote == type;
      final count = hakli ? widget.hakliCount : widget.haksizCount;
      final word = hakli ? 'Haklı' : 'Haksız';
      final label = selected && widget.unvoteLabels
          ? '$word oyunu kaldır'
          : '$word oyu ver';
      Widget capsule = _VoteCapsule(
        size: m.voteCapsule,
        icon: hakli ? Icons.check_rounded : Icons.close_rounded,
        iconSize: widget.size == VoteRailSize.compact ? 24 : 26,
        count: count,
        selected: selected,
        fill: kc.voteFill(hakli),
        capsuleColor: kc.capsule,
        textColor: kc.onCapsuleText,
        iconColor: kc.onCapsuleVoteIcon(hakli),
        onFill: kc.onColor,
        pending: widget.pendingVote == type,
      );
      capsule = Stack(
        alignment: Alignment.center,
        clipBehavior: Clip.none,
        children: [
          if (hakli && widget.pulseHakli)
            _PulseRing(
              animation: _coach,
              size: m.voteCapsule,
              color: kc.hakliFill,
              maxScale: 1.35,
              maxOpacity: 0.5,
            ),
          if (_burstSide == type)
            _PulseRing(
              animation: _burst,
              size: m.voteCapsule,
              color: kc.voteFill(hakli),
              maxScale: 1.45,
              maxOpacity: 0.45,
            ),
          capsule,
        ],
      );
      if (!votingEnabled) capsule = Opacity(opacity: 0.6, child: capsule);
      return KararPressable(
        key: hakli ? widget.hakliKey : widget.haksizKey,
        onTap: votingEnabled && !pending ? () => _tapVote(type) : null,
        semanticLabel: label,
        semanticValue: '$count oy',
        selected: selected,
        pressedScale: 0.94,
        hoverScale: 1.04,
        focusRadius: m.voteCapsule.width / 2,
        child: capsule,
      );
    }

    final children = <Widget>[
      voteCapsule(VoteType.hakli),
      SizedBox(height: m.gap),
      voteCapsule(VoteType.haksiz),
      SizedBox(
        height: m.dividerBlock,
        child: Center(
          child: ExcludeSemantics(
            child: Container(
              width: 22,
              height: 2,
              decoration: BoxDecoration(
                color: KararGlass.railDivider,
                borderRadius: KararRadius.all(1),
              ),
            ),
          ),
        ),
      ),
      KararPressable(
        onTap: widget.onComment,
        tooltip: 'Yorumlar',
        semanticLabel: 'Yorumlar, ${widget.commentCount}',
        pressedScale: 0.94,
        hoverScale: 1.04,
        focusRadius: m.commentCapsule.width / 2,
        child: _CountCapsule(
          size: m.commentCapsule,
          icon: Icons.chat_bubble_outline_rounded,
          count: widget.commentCount,
          color: kc.capsule,
          textColor: kc.onCapsuleText,
        ),
      ),
      if (widget.onSave != null) ...[
        SizedBox(height: m.gap),
        KararPressable(
          onTap: () {
            KararHaptics.toggle();
            widget.onSave!();
          },
          tooltip: 'Kaydet',
          selected: widget.isSaved,
          pressedScale: 0.94,
          hoverScale: 1.04,
          focusShape: BoxShape.circle,
          child: _RailCircle(
            diameter: m.circle,
            solid: widget.isSaved,
            photo: widget.liveBlur,
            child: Icon(
              widget.isSaved
                  ? Icons.bookmark_rounded
                  : Icons.bookmark_border_rounded,
              size: 22,
              // `primary`: #7D50F0 on the white capsule, lavender #A98BFF
              // (≥ 4.68:1) on the dark one.
              color: widget.isSaved ? kc.primary : kc.onColor,
            ),
          ),
        ),
      ],
      if (widget.onShare != null) ...[
        SizedBox(height: m.gap),
        KararPressable(
          onTap: widget.onShare,
          tooltip: 'Paylaş',
          pressedScale: 0.94,
          hoverScale: 1.04,
          focusShape: BoxShape.circle,
          child: _RailCircle(
            diameter: m.circle,
            photo: widget.liveBlur,
            child: Icon(Icons.send_rounded, size: 21, color: kc.onColor),
          ),
        ),
      ],
    ];

    return GlassRail(
      width: m.width,
      height: m.height,
      liveBlur: widget.liveBlur,
      child: Column(mainAxisSize: MainAxisSize.min, children: children),
    );
  }
}

class _VoteCapsule extends StatelessWidget {
  const _VoteCapsule({
    required this.size,
    required this.icon,
    required this.iconSize,
    required this.count,
    required this.selected,
    required this.fill,
    required this.capsuleColor,
    required this.textColor,
    required this.iconColor,
    required this.onFill,
    required this.pending,
  });

  final Size size;
  final IconData icon;
  final double iconSize;
  final int count;
  final bool selected;
  final Color fill;
  final Color capsuleColor;
  final Color textColor;

  /// Vote icon colour on the unselected capsule.
  final Color iconColor;

  /// Content colour on the selected (vote-filled) capsule.
  final Color onFill;
  final bool pending;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    final fg = selected ? onFill : textColor;
    final glyph = selected ? onFill : iconColor;
    final radius = size.width / 2;
    Widget capsule = AnimatedContainer(
      duration: KararMotion.d(context, KararMotion.base),
      curve: KararMotion.standard,
      width: size.width,
      height: size.height,
      decoration: BoxDecoration(
        color: selected ? fill : capsuleColor,
        borderRadius: BorderRadius.circular(radius),
        // Selected: 2 px ring (white in light, #F4F3F8 in dark).
        // Unselected: the dark-mode hairline (none in light).
        border: selected
            ? Border.all(
                color: kc.capsuleSelectedRing,
                width: 2,
                strokeAlign: BorderSide.strokeAlignOutside,
              )
            : kc.capsuleBorder,
        boxShadow: selected
            ? KararShadow.capsuleSelectedOf(brightness)
            : KararShadow.capsuleOf(brightness),
      ),
      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 6),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: iconSize, color: glyph),
          const SizedBox(height: 3),
          Flexible(child: _RollingCount(count: count, color: fg)),
        ],
      ),
    );
    if (pending) {
      capsule = Stack(
        clipBehavior: Clip.none,
        alignment: Alignment.center,
        children: [
          capsule,
          Positioned(
            left: -4,
            top: -4,
            right: -4,
            bottom: -4,
            child: _PendingRing(radius: radius + 4, color: onFill),
          ),
        ],
      );
    }
    return capsule;
  }
}

class _CountCapsule extends StatelessWidget {
  const _CountCapsule({
    required this.size,
    required this.icon,
    required this.count,
    required this.color,
    required this.textColor,
  });

  final Size size;
  final IconData icon;
  final int count;
  final Color color;
  final Color textColor;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size.width,
      height: size.height,
      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 6),
      decoration: BoxDecoration(
        color: color,
        borderRadius: BorderRadius.circular(size.width / 2),
        border: context.kc.capsuleBorder,
        boxShadow: KararShadow.capsuleOf(Theme.of(context).brightness),
      ),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 22, color: textColor),
          const SizedBox(height: 3),
          Flexible(child: _RollingCount(count: count, color: textColor)),
        ],
      ),
    );
  }
}

/// Kaydet / Paylaş circle: glass (translucent black + white stroke, no
/// extra blur — the rail already carries the card's one live blur), or a
/// solid capsule when [solid] (saved state).
class _RailCircle extends StatelessWidget {
  const _RailCircle({
    required this.diameter,
    required this.child,
    this.solid = false,
    this.photo = false,
  });

  final double diameter;
  final Widget child;
  final bool solid;

  /// On a photo (darker glass for contrast over bright images).
  final bool photo;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final highContrast = MediaQuery.maybeHighContrastOf(context) ?? false;
    final Color fill;
    if (solid) {
      fill = kc.capsule;
    } else if (highContrast) {
      fill = KararGlass.chipHighContrast;
    } else {
      fill = photo ? KararGlass.railCirclePhoto : KararGlass.railCircle;
    }
    return Container(
      width: diameter,
      height: diameter,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: fill,
        shape: BoxShape.circle,
        border: solid
            ? kc.capsuleBorder
            : Border.all(color: KararGlass.stroke),
        boxShadow: solid
            ? KararShadow.capsuleOf(Theme.of(context).brightness)
            : null,
      ),
      child: child,
    );
  }
}

/// Count that rolls up when it changes (180 ms slide-up), tabular figures,
/// scaled down to fit the capsule at large text scales.
class _RollingCount extends StatelessWidget {
  const _RollingCount({required this.count, required this.color});

  final int count;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final text = Text(
      formatCompactCount(count),
      key: ValueKey(count),
      maxLines: 1,
      style: AppTypography.meta.copyWith(
        fontWeight: FontWeight.w800,
        color: color,
        letterSpacing: 0,
        height: 1.1,
        fontFeatures: AppTypography.tabular,
      ),
    );
    final reduce = KararMotion.reduce(context);
    return FittedBox(
      fit: BoxFit.scaleDown,
      child: AnimatedSwitcher(
        duration: reduce ? Duration.zero : const Duration(milliseconds: 180),
        switchInCurve: KararMotion.standard,
        switchOutCurve: KararMotion.exit,
        transitionBuilder: (child, animation) {
          final incoming = child.key == ValueKey(count);
          final offset = Tween<Offset>(
            begin: Offset(0, incoming ? 0.6 : -0.6),
            end: Offset.zero,
          ).animate(animation);
          return ClipRect(
            child: SlideTransition(
              position: offset,
              child: FadeTransition(opacity: animation, child: child),
            ),
          );
        },
        child: text,
      ),
    );
  }
}

/// Expanding, fading stadium behind a capsule (vote burst / coachmark pulse).
class _PulseRing extends StatelessWidget {
  const _PulseRing({
    required this.animation,
    required this.size,
    required this.color,
    required this.maxScale,
    required this.maxOpacity,
  });

  final Animation<double> animation;
  final Size size;
  final Color color;
  final double maxScale;
  final double maxOpacity;

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: ExcludeSemantics(
        child: AnimatedBuilder(
          animation: animation,
          builder: (context, _) {
            final t = KararMotion.standard.transform(animation.value);
            final opacity =
                animation.isAnimating ? (1 - t) * maxOpacity : 0.0;
            return Transform.scale(
              scale: 1 + (maxScale - 1) * t,
              child: Container(
                width: size.width,
                height: size.height,
                decoration: BoxDecoration(
                  color: color.withValues(alpha: opacity.clamp(0.0, 1.0)),
                  borderRadius: BorderRadius.circular(size.width / 2),
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}

/// 2 px ring (white on colour) travelling around a capsule while a vote is
/// pending.
class _PendingRing extends StatefulWidget {
  const _PendingRing({required this.radius, required this.color});

  final double radius;
  final Color color;

  @override
  State<_PendingRing> createState() => _PendingRingState();
}

class _PendingRingState extends State<_PendingRing>
    with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 900),
  );

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (KararMotion.reduce(context)) {
      _c.stop();
    } else if (!_c.isAnimating) {
      _c.repeat();
    }
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final reduce = KararMotion.reduce(context);
    return IgnorePointer(
      child: ExcludeSemantics(
        child: AnimatedBuilder(
          animation: _c,
          builder: (context, _) => CustomPaint(
            painter: _PendingRingPainter(
              progress: _c.value,
              radius: widget.radius,
              full: reduce,
              color: widget.color,
            ),
          ),
        ),
      ),
    );
  }
}

class _PendingRingPainter extends CustomPainter {
  const _PendingRingPainter({
    required this.progress,
    required this.radius,
    required this.full,
    required this.color,
  });

  final double progress;
  final double radius;
  final bool full;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final rrect = RRect.fromRectAndRadius(
      (Offset.zero & size).deflate(1),
      Radius.circular(radius),
    );
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2
      ..strokeCap = StrokeCap.round;
    final path = Path()..addRRect(rrect);
    if (full) {
      canvas.drawPath(path, paint..color = color.withValues(alpha: 0.7));
      return;
    }
    for (final metric in path.computeMetrics()) {
      final length = metric.length;
      final segment = length * 0.32;
      final start = progress * length;
      final end = start + segment;
      canvas.drawPath(metric.extractPath(start, math.min(end, length)), paint);
      if (end > length) {
        canvas.drawPath(metric.extractPath(0, end - length), paint);
      }
    }
  }

  @override
  bool shouldRepaint(_PendingRingPainter oldDelegate) =>
      oldDelegate.progress != progress ||
      oldDelegate.radius != radius ||
      oldDelegate.full != full ||
      oldDelegate.color != color;
}
