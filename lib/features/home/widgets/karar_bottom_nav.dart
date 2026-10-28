// The notch shape (NavNotchClipper) is adapted from `ClipStatusBar` in
// rrdhoi/SocialMediaApp (https://github.com/rrdhoi/SocialMediaApp), used under
// the MIT License:
//
//   MIT License
//
//   Copyright (c) 2022 Muhammad Ridhoi
//
//   Permission is hereby granted, free of charge, to any person obtaining a
//   copy of this software and associated documentation files (the
//   "Software"), to deal in the Software without restriction, including
//   without limitation the rights to use, copy, modify, merge, publish,
//   distribute, sublicense, and/or sell copies of the Software, and to
//   permit persons to whom the Software is furnished to do so, subject to
//   the following conditions:
//
//   The above copyright notice and this permission notice shall be included
//   in all copies or substantial portions of the Software.
//
//   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
//   OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//   MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
//   IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
//   CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
//   TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
//   SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Changes: fixed pixel fillets instead of height fractions, rotated so the
// attached edge is the bottom (a tab rising above the nav pill), painted
// with a shadow, an optional active glow, an optional hairline and a focus
// ring.

import 'dart:ui' show ImageFilter;

import 'package:flutter/foundation.dart' show listEquals;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../core/layout/breakpoints.dart';
import '../../../core/theme/app_typography.dart';
import '../../../core/theme/karar_tokens.dart';
import '../../../core/theme/motion.dart';
import '../../../shared/widgets/karar_pressable.dart';

/// Outline of the "+" create tab. Attached edge = bottom: the shape flares
/// into the nav pill with [fillet]-px curves on both sides and has
/// [corner]-px rounded top corners. `W` = width including the fillets,
/// `R` = rise above the pill.
class NavNotchClipper extends CustomClipper<Path> {
  const NavNotchClipper({this.fillet = 16, this.corner = 24});

  final double fillet;
  final double corner;

  /// Closed outline for [s].
  static Path pathFor(Size s, {double fillet = 16, double corner = 24}) =>
      _outline(s, fillet, corner)..close();

  /// The outline without the bottom (attached) edge — used for strokes so a
  /// ring never draws a line across the pill.
  static Path openPathFor(Size s, {double fillet = 16, double corner = 24}) =>
      _outline(s, fillet, corner);

  static Path _outline(Size s, double f, double c) {
    final w = s.width, r = s.height;
    return Path()
      ..moveTo(w, r)
      ..quadraticBezierTo(w - f, r, w - f, 0.6 * r)
      ..lineTo(w - f, 0.5 * r)
      ..quadraticBezierTo(w - f, 0, w - f - c, 0)
      ..lineTo(f + c, 0)
      ..quadraticBezierTo(f, 0, f, 0.5 * r)
      ..lineTo(f, 0.6 * r)
      ..quadraticBezierTo(f, r, 0, r);
  }

  @override
  Path getClip(Size size) => pathFor(size, fillet: fillet, corner: corner);

  @override
  bool shouldReclip(NavNotchClipper oldClipper) =>
      oldClipper.fillet != fillet || oldClipper.corner != corner;
}

/// Floating pill navigation for mobile/tablet (spec §5.1).
///
/// Tabs left→right: Anasayfa (branch 0), Keşfet (5), centre gap, Bildirimler
/// (2, unread badge), Profilim (3). The notched "+" tab (Paylaş, branch 1)
/// rises above the pill's centre gap; notch + gap form one ≥ 56 × 104 target.
///
/// The widget is transparent outside the pill and the notch; its height
/// ([heightFor]) is what the shell's `extendBody` adds to the content's
/// bottom padding (`context.navBottomInset`).
class KararBottomNav extends StatefulWidget {
  const KararBottomNav({
    super.key,
    required this.currentIndex,
    required this.onSelect,
    required this.unreadCount,
    this.hidden = false,
    this.overMedia = false,
  });

  /// Current shell branch index.
  final int currentIndex;

  /// Called with a shell branch index.
  final ValueChanged<int> onSelect;
  final int unreadCount;

  /// Slides the nav out (the on-screen keyboard is up — on mobile web the
  /// nav would otherwise sit on top of it).
  final bool hidden;

  /// The page behind the nav is a full-bleed photo/scrim (Keşfet): in light
  /// the notch gets a light hairline so its silhouette never melts into a
  /// dark backdrop (the ink notch on a near-black scrim is 1.05:1). Dark
  /// always draws its own edge (see [_CreateNotch]).
  final bool overMedia;

  static const double pillHeight = 70;
  static const double notchRise = 34;
  static const double notchWidth = 112;
  static const double centerGap = 56;
  static const double bottomMargin = 10;
  static const double sideMargin = 12;
  static const double tabletMaxWidth = 560;

  /// Total height of the nav for a device bottom safe-area inset.
  static double heightFor(double safeBottom) =>
      pillHeight + notchRise + bottomMargin + safeBottom;

  @override
  State<KararBottomNav> createState() => _KararBottomNavState();
}

class _KararBottomNavState extends State<KararBottomNav> {
  static const pillHeight = KararBottomNav.pillHeight;
  static const notchRise = KararBottomNav.notchRise;
  static const sideMargin = KararBottomNav.sideMargin;
  static const bottomMargin = KararBottomNav.bottomMargin;

  /// Shared by the notch and the pill's centre gap (one button).
  final _createPressed = ValueNotifier<bool>(false);

  @override
  void dispose() {
    _createPressed.dispose();
    super.dispose();
  }

  /// Which of the four tabs is active (-1 = none). Create (1) has its own
  /// notch state; Search (4) has no tab here, so no tab is highlighted
  /// rather than suggesting the feed. Unknown branches fall back to
  /// Anasayfa.
  int get _selectedTab => switch (widget.currentIndex) {
        0 => 0,
        5 => 1,
        2 => 2,
        3 => 3,
        1 || 4 => -1,
        _ => 0,
      };

  void _create() {
    KararHaptics.tap();
    widget.onSelect(1);
  }

  @override
  Widget build(BuildContext context) {
    final safeBottom = MediaQuery.paddingOf(context).bottom;
    final selected = _selectedTab;
    final unreadCount = widget.unreadCount;
    final hidden = widget.hidden;

    void select(int branch) {
      KararHaptics.toggle();
      widget.onSelect(branch);
    }

    final tabs = <Widget>[
      Expanded(
        child: _NavTab(
          label: 'Anasayfa',
          icon: Icons.home_outlined,
          activeIcon: Icons.home_rounded,
          active: selected == 0,
          onTap: () => select(0),
        ),
      ),
      Expanded(
        child: _NavTab(
          label: 'Keşfet',
          icon: Icons.explore_outlined,
          activeIcon: Icons.explore_rounded,
          active: selected == 1,
          onTap: () => select(5),
        ),
      ),
      _CreateGapTarget(pressed: _createPressed, onTap: _create),
      Expanded(
        child: _NavTab(
          label: 'Bildirimler',
          icon: Icons.notifications_none_rounded,
          activeIcon: Icons.notifications_rounded,
          active: selected == 2,
          badge: unreadCount,
          onTap: () => select(2),
        ),
      ),
      Expanded(
        child: _NavTab(
          label: 'Profilim',
          icon: Icons.person_outline_rounded,
          activeIcon: Icons.person_rounded,
          active: selected == 3,
          onTap: () => select(3),
        ),
      ),
    ];

    final body = SizedBox(
      height: pillHeight + notchRise,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          // The notch shadow (and the active glow) sit behind the pill so
          // they never smear the pill's surface.
          Positioned(
            top: 0,
            left: 0,
            right: 0,
            child: Center(
              child: _NotchShadow(active: widget.currentIndex == 1),
            ),
          ),
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            height: pillHeight,
            child: _Pill(
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 8),
                child: Row(children: tabs),
              ),
            ),
          ),
          Positioned(
            top: 0,
            left: 0,
            right: 0,
            child: Center(
              child: _CreateNotch(
                active: widget.currentIndex == 1,
                overMedia: widget.overMedia,
                pressed: _createPressed,
                onTap: _create,
              ),
            ),
          ),
        ],
      ),
    );

    final nav = SizedBox(
      height: KararBottomNav.heightFor(safeBottom),
      child: Align(
        alignment: Alignment.bottomCenter,
        child: ConstrainedBox(
          constraints: BoxConstraints(
            maxWidth: context.isMobile
                ? double.infinity
                : KararBottomNav.tabletMaxWidth + 2 * sideMargin,
          ),
          child: Padding(
            padding: EdgeInsets.fromLTRB(
              sideMargin,
              0,
              sideMargin,
              bottomMargin + safeBottom,
            ),
            child: body,
          ),
        ),
      ),
    );

    return AnimatedSlide(
      offset: hidden ? const Offset(0, 1.4) : Offset.zero,
      duration: KararMotion.d(context, KararMotion.nav),
      curve: KararMotion.standard,
      child: IgnorePointer(
        ignoring: hidden,
        child: ExcludeSemantics(
          excluding: hidden,
          child: Semantics(
            container: true,
            explicitChildNodes: true,
            child: nav,
          ),
        ),
      ),
    );
  }
}

/// The pill: light solid white + float shadow; dark #1F1C2A @ 94 % with a
/// σ 20 backdrop blur and a white 7 % hairline. High contrast: solid.
class _Pill extends StatelessWidget {
  const _Pill({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    final highContrast = MediaQuery.maybeHighContrastOf(context) ?? false;
    final radius = KararRadius.all(KararRadius.panel);
    final hairline = KararShadow.hairlineFor(KararShadowKind.float, brightness);
    final fill = highContrast
        ? (kc.isDark ? kc.elevated : kc.surface)
        : kc.navPill;

    Widget pill = DecoratedBox(
      decoration: BoxDecoration(
        color: fill,
        borderRadius: radius,
        border: hairline == null ? null : Border.all(color: hairline),
      ),
      child: child,
    );

    if (kc.isDark && !highContrast) {
      pill = ClipRRect(
        borderRadius: radius,
        child: BackdropFilter(
          filter: ImageFilter.blur(
            sigmaX: KararGlass.navBlur,
            sigmaY: KararGlass.navBlur,
          ),
          child: pill,
        ),
      );
    }

    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: radius,
        boxShadow: KararShadow.float(brightness),
      ),
      child: pill,
    );
  }
}

class _NavTab extends StatelessWidget {
  const _NavTab({
    required this.label,
    required this.icon,
    required this.activeIcon,
    required this.active,
    required this.onTap,
    this.badge = 0,
  });

  final String label;
  final IconData icon;
  final IconData activeIcon;
  final bool active;
  final VoidCallback onTap;
  final int badge;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final iconColor = active ? kc.navActive : kc.text2;
    final labelColor = active ? kc.primaryText : kc.text2;
    final tabBg = active ? kc.primaryContainer : kc.navPill;

    Widget iconWidget = AnimatedSwitcher(
      duration: KararMotion.d(context, KararMotion.base),
      switchInCurve: KararMotion.spring,
      switchOutCurve: KararMotion.exit,
      transitionBuilder: (child, animation) => ScaleTransition(
        scale: Tween<double>(begin: 0.7, end: 1).animate(animation),
        child: FadeTransition(opacity: animation, child: child),
      ),
      child: Icon(
        active ? activeIcon : icon,
        key: ValueKey(active),
        size: 24,
        color: iconColor,
      ),
    );

    if (badge > 0) {
      iconWidget = Stack(
        clipBehavior: Clip.none,
        children: [
          iconWidget,
          Positioned(
            top: -7,
            left: 14,
            child: _NavBadge(count: badge, ringColor: tabBg),
          ),
        ],
      );
    }

    final content = Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        iconWidget,
        const SizedBox(height: 3),
        Flexible(
          child: FittedBox(
            fit: BoxFit.scaleDown,
            child: Text(
              label,
              maxLines: 1,
              style: AppTypography.meta.copyWith(
                fontWeight: active ? FontWeight.w800 : FontWeight.w700,
                letterSpacing: 0,
                color: labelColor,
              ),
            ),
          ),
        ),
      ],
    );

    final reduce = KararMotion.reduce(context);
    final indicator = AnimatedScale(
      scale: active ? 1 : 0.9,
      duration: reduce ? Duration.zero : KararMotion.base,
      curve: KararMotion.standard,
      child: AnimatedOpacity(
        opacity: active ? 1 : 0,
        duration: KararMotion.fade(context, KararMotion.base),
        curve: KararMotion.standard,
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: kc.primaryContainer,
            borderRadius: KararRadius.all(KararRadius.navItem),
          ),
        ),
      ),
    );

    return KararPressable(
      onTap: onTap,
      semanticLabel: label,
      semanticValue: badge > 0 ? '${badge.clamp(0, 99)}' : null,
      selected: active,
      pressedScale: 0.96,
      focusRadius: KararRadius.navItem,
      minSize: const Size(KararSpace.minTap, 56),
      child: SizedBox(
        height: 56,
        width: double.infinity,
        child: Stack(
          fit: StackFit.expand,
          children: [
            Positioned.fill(child: ExcludeSemantics(child: indicator)),
            content,
          ],
        ),
      ),
    );
  }
}

/// Unread badge: `haksizFill` with white 11/800 digits (clamped to 99) and a
/// 2 px ring in the tab background colour.
class _NavBadge extends StatelessWidget {
  const _NavBadge({required this.count, required this.ringColor});

  final int count;
  final Color ringColor;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return ExcludeSemantics(
      child: Container(
        constraints: const BoxConstraints(minWidth: 22, minHeight: 22),
        padding: const EdgeInsets.symmetric(horizontal: 4),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: kc.haksizFill,
          borderRadius: KararRadius.all(KararRadius.pill),
          border: Border.all(color: ringColor, width: 2),
        ),
        child: Text(
          '${count.clamp(0, 99)}',
          textScaler: TextScaler.noScaling,
          style: AppTypography.micro.copyWith(
            color: kc.onColor,
            letterSpacing: 0,
            height: 1,
            fontFeatures: AppTypography.tabular,
          ),
        ),
      ),
    );
  }
}

/// Float shadow of the notch, painted behind the pill. While the create
/// branch is open ([active]) a soft brand glow rises around the notch
/// instead of an outline ring (an outline read as a sticker edge and, on
/// the violet dark notch, was only 1.8:1 against the fill).
///
/// Dark adds a tight canvas halo hugging the outline, so the violet notch
/// keeps its silhouette over a violet card scrolling behind the nav (the
/// bare fillets were 1.5–1.9:1 against an İş Hayatı card).
class _NotchShadow extends StatelessWidget {
  const _NotchShadow({required this.active});

  final bool active;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final shadows = [
      ...KararShadow.float(kc.brightness),
      if (kc.isDark)
        BoxShadow(
          color: kc.canvas.withValues(alpha: 0.85),
          offset: const Offset(0, -2),
          blurRadius: 14,
        ),
      if (active)
        BoxShadow(
          color: kc.primaryFill.withValues(alpha: kc.isDark ? 0.60 : 0.50),
          blurRadius: 18,
        ),
    ];
    return IgnorePointer(
      child: ExcludeSemantics(
        child: CustomPaint(
          size: const Size(
            KararBottomNav.notchWidth,
            KararBottomNav.notchRise,
          ),
          painter: _NotchShadowPainter(shadows),
        ),
      ),
    );
  }
}

class _NotchShadowPainter extends CustomPainter {
  const _NotchShadowPainter(this.shadows);

  final List<BoxShadow> shadows;

  @override
  void paint(Canvas canvas, Size size) {
    final path = NavNotchClipper.pathFor(size);
    for (final s in shadows) {
      canvas.drawPath(
        path.shift(s.offset),
        Paint()
          ..color = s.color
          ..maskFilter = MaskFilter.blur(BlurStyle.normal, s.blurSigma),
      );
    }
  }

  @override
  bool shouldRepaint(_NotchShadowPainter oldDelegate) =>
      !listEquals(oldDelegate.shadows, shadows);
}

/// The "+" create tab (112 × 34) rising above the pill. Together with
/// [_CreateGapTarget] (the pill's 56 px centre gap, full pill height) it
/// forms one ≥ 56 × 104 button that never overlaps the tabs. The notch's
/// hit box is its 80 px body, so the transparent fillets pass taps through.
class _CreateNotch extends StatefulWidget {
  const _CreateNotch({
    required this.active,
    required this.overMedia,
    required this.pressed,
    required this.onTap,
  });

  final bool active;
  final bool overMedia;
  final ValueNotifier<bool> pressed;
  final VoidCallback onTap;

  @override
  State<_CreateNotch> createState() => _CreateNotchState();
}

class _CreateNotchState extends State<_CreateNotch> {
  bool _focused = false;

  void _setPressed(bool value) => widget.pressed.value = value;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final reduce = KararMotion.reduce(context);
    const size = Size(KararBottomNav.notchWidth, KararBottomNav.notchRise);
    const fillet = 16.0;

    final visual = ValueListenableBuilder<bool>(
      valueListenable: widget.pressed,
      builder: (context, pressed, child) => AnimatedScale(
        scale: pressed && !reduce ? 0.92 : 1,
        alignment: Alignment.bottomCenter,
        duration: reduce
            ? Duration.zero
            : (pressed ? KararMotion.press : KararMotion.release),
        curve: pressed ? KararMotion.standard : KararMotion.spring,
        child: child,
      ),
      // Light: ink notch with a white "+" (reference). Dark: `ink` is the
      // brand violet, never an off-white block. Active = the glow painted
      // by _NotchShadow behind the pill, no outline.
      //
      // Dark always gets its own silhouette whatever scrolls behind it: a
      // 2 px canvas ring outside the outline plus a white 22 % hairline on
      // the edge (violet card | dark ring | light edge | violet notch).
      child: CustomPaint(
        size: size,
        painter: _NotchPainter(
          fill: kc.ink,
          focus: _focused ? kc.primary : null,
          hairline: kc.isDark
              ? kc.onColor.withValues(alpha: 0.22)
              : (widget.overMedia ? kc.onColorSoft : null),
          ring: kc.isDark ? kc.canvas : null,
        ),
        child: SizedBox.fromSize(
          size: size,
          child: Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Center(
              child: Icon(Icons.add_rounded, size: 26, color: kc.onInk),
            ),
          ),
        ),
      ),
    );

    final target = Semantics(
      container: true,
      button: true,
      selected: widget.active,
      label: 'Paylaş',
      onTap: widget.onTap,
      excludeSemantics: true,
      child: Tooltip(
        message: 'Paylaş',
        excludeFromSemantics: true,
        child: FocusableActionDetector(
          mouseCursor: SystemMouseCursors.click,
          onShowFocusHighlight: (value) {
            if (_focused != value) setState(() => _focused = value);
          },
          shortcuts: const {
            SingleActivator(LogicalKeyboardKey.enter): ActivateIntent(),
            SingleActivator(LogicalKeyboardKey.numpadEnter): ActivateIntent(),
            SingleActivator(LogicalKeyboardKey.space): ActivateIntent(),
          },
          actions: {
            ActivateIntent: CallbackAction<ActivateIntent>(
              onInvoke: (_) {
                widget.onTap();
                return null;
              },
            ),
          },
          child: GestureDetector(
            behavior: HitTestBehavior.opaque,
            excludeFromSemantics: true,
            onTapDown: (_) => _setPressed(true),
            onTapUp: (_) => _setPressed(false),
            onTapCancel: () => _setPressed(false),
            onTap: widget.onTap,
            child: const SizedBox.expand(),
          ),
        ),
      ),
    );

    return SizedBox.fromSize(
      size: size,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          Positioned.fill(child: IgnorePointer(child: visual)),
          Positioned(
            left: fillet,
            right: fillet,
            top: 0,
            bottom: 0,
            child: target,
          ),
        ],
      ),
    );
  }
}

/// The pill's 56 px centre gap: part of the create button (same action and
/// press feedback as the notch; the notch carries the semantics).
class _CreateGapTarget extends StatelessWidget {
  const _CreateGapTarget({required this.pressed, required this.onTap});

  final ValueNotifier<bool> pressed;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return ExcludeSemantics(
      child: MouseRegion(
        cursor: SystemMouseCursors.click,
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTapDown: (_) => pressed.value = true,
          onTapUp: (_) => pressed.value = false,
          onTapCancel: () => pressed.value = false,
          onTap: onTap,
          child: const SizedBox(
            width: KararBottomNav.centerGap,
            height: KararBottomNav.pillHeight,
          ),
        ),
      ),
    );
  }
}

class _NotchPainter extends CustomPainter {
  const _NotchPainter({
    required this.fill,
    this.focus,
    this.hairline,
    this.ring,
  });

  final Color fill;

  /// Keyboard focus ring (2 px outside the outline).
  final Color? focus;

  /// 1 px edge on the outline (dark: always; light: over a full-bleed
  /// backdrop such as Keşfet).
  final Color? hairline;

  /// 2 px separation ring outside the outline (dark: canvas colour), so the
  /// notch never fuses with a same-hued card behind it. Replaced by the
  /// focus ring while focused.
  final Color? ring;

  @override
  void paint(Canvas canvas, Size size) {
    final open = NavNotchClipper.openPathFor(size);
    final focus = this.focus;
    final outer = focus ?? ring;
    if (outer != null) {
      // Centred 4 px stroke under the fill = 2 px visible outside. Clipped
      // at the pill's top edge so the fillet ends never cut into the pill.
      canvas.save();
      canvas.clipRect(Rect.fromLTRB(-4, -4, size.width + 4, size.height));
      canvas.drawPath(
        open,
        Paint()
          ..color = outer
          ..style = PaintingStyle.stroke
          ..strokeWidth = 4
          ..strokeJoin = StrokeJoin.round,
      );
      canvas.restore();
    }
    canvas.drawPath(NavNotchClipper.pathFor(size), Paint()..color = fill);
    final hairline = this.hairline;
    if (hairline != null && focus == null) {
      canvas.drawPath(
        open,
        Paint()
          ..color = hairline
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1
          ..strokeJoin = StrokeJoin.round,
      );
    }
  }

  @override
  bool shouldRepaint(_NotchPainter oldDelegate) =>
      oldDelegate.fill != fill ||
      oldDelegate.focus != focus ||
      oldDelegate.hairline != hairline ||
      oldDelegate.ring != ring;
}
