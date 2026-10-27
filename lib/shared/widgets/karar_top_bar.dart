import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';

/// Scroll-aware top-bar background (spec §2.8 "Scrolled top bar").
///
/// Pass it as an `AppBar.flexibleSpace`:
///
/// ```dart
/// AppBar(
///   flexibleSpace: const KararTopBarBackground(),
///   title: …,
/// )
/// ```
///
/// * At rest: the plain canvas (the bar melts into the page, as in the
///   reference).
/// * Once content scrolls under it: canvas @ 88 % + backdrop blur σ 16 and a
///   1 px `outlineVariant` hairline at the bottom edge, so cards passing
///   beneath read as sliding under frosted glass instead of being sliced at
///   a knife edge. The blur only shows content when the Scaffold sets
///   `extendBodyBehindAppBar: true`; otherwise the bar is simply the canvas
///   with the hairline.
/// * High contrast: no translucency (solid canvas + hairline).
///
/// It follows the same scroll signal as `AppBar` itself (vertical
/// [ScrollUpdateNotification]s of depth 0 reported to the enclosing
/// [ScrollNotificationObserver], or `FlexibleSpaceBarSettings` inside a
/// `SliverAppBar`), so it changes state exactly when the app bar does.
class KararTopBarBackground extends StatefulWidget {
  const KararTopBarBackground({
    super.key,
    this.notificationPredicate = defaultScrollNotificationPredicate,
  });

  /// Which scroll notifications count (same as `AppBar.notificationPredicate`).
  final ScrollNotificationPredicate notificationPredicate;

  @override
  State<KararTopBarBackground> createState() => _KararTopBarBackgroundState();
}

class _KararTopBarBackgroundState extends State<KararTopBarBackground> {
  ScrollNotificationObserverState? _observer;
  bool _scrolledUnder = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _observer?.removeListener(_handleScroll);
    _observer = ScrollNotificationObserver.maybeOf(context);
    _observer?.addListener(_handleScroll);
  }

  @override
  void dispose() {
    _observer?.removeListener(_handleScroll);
    _observer = null;
    super.dispose();
  }

  void _handleScroll(ScrollNotification notification) {
    if (notification is! ScrollUpdateNotification ||
        !widget.notificationPredicate(notification)) {
      return;
    }
    final metrics = notification.metrics;
    final bool next;
    switch (metrics.axisDirection) {
      case AxisDirection.up:
        next = metrics.extentAfter > 0;
      case AxisDirection.down:
        next = metrics.extentBefore > 0;
      case AxisDirection.left:
      case AxisDirection.right:
        return;
    }
    if (next != _scrolledUnder && mounted) {
      setState(() => _scrolledUnder = next);
    }
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final settings =
        context.dependOnInheritedWidgetOfExactType<FlexibleSpaceBarSettings>();
    final scrolled = settings?.isScrolledUnder ?? _scrolledUnder;
    final highContrast = MediaQuery.maybeHighContrastOf(context) ?? false;
    final duration = KararMotion.fade(context, KararMotion.fast);

    Widget fill;
    if (scrolled && !highContrast) {
      fill = ClipRect(
        child: BackdropFilter(
          filter: ImageFilter.blur(
            sigmaX: KararGlass.barBlur,
            sigmaY: KararGlass.barBlur,
          ),
          child: ColoredBox(
            color: kc.canvas.withValues(alpha: KararGlass.barAlpha),
          ),
        ),
      );
    } else {
      fill = ColoredBox(color: kc.canvas);
    }

    return ExcludeSemantics(
      child: Stack(
        fit: StackFit.expand,
        children: [
          fill,
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            height: 1,
            child: AnimatedOpacity(
              opacity: scrolled ? 1 : 0,
              duration: duration,
              curve: KararMotion.standard,
              child: ColoredBox(color: kc.outlineVariant),
            ),
          ),
        ],
      ),
    );
  }
}
