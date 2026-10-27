import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';

/// Interaction shell for Karar's custom controls (chips, circle buttons,
/// rail capsules, text tabs, pills).
///
/// * Press feedback is a scale (default 0.96, `press` 90 ms) released with
///   the `spring` curve; optional hover scale on pointer devices.
/// * Keyboard: focusable, activates with Enter/Space, and paints the visible
///   focus ring (3 px `primary`, 3 px gap) around [child].
/// * Hit area: [child] is centred inside a box of at least [minSize]
///   (44 × 44 by default), so small visuals stay accessible.
/// * Semantics: a single button node with [semanticLabel] / [selected].
///   Child semantics are excluded when a label is given.
/// * Reduce motion: scales are skipped.
class KararPressable extends StatefulWidget {
  const KararPressable({
    super.key,
    required this.child,
    required this.onTap,
    this.onLongPress,
    this.semanticLabel,
    this.semanticValue,
    this.semanticHint,
    this.tooltip,
    this.selected,
    this.pressedScale = 0.96,
    this.hoverScale = 1.0,
    this.focusRadius,
    this.focusShape = BoxShape.rectangle,
    this.minSize = const Size(KararSpace.minTap, KararSpace.minTap),
    this.excludeChildSemantics = true,
    this.focusNode,
    this.autofocus = false,
  });

  final Widget child;

  /// Null disables the control (no focus, no feedback).
  final VoidCallback? onTap;
  final VoidCallback? onLongPress;

  /// Accessible name. Defaults to [tooltip].
  final String? semanticLabel;
  final String? semanticValue;
  final String? semanticHint;

  /// Optional tooltip (shown on long-press / hover).
  final String? tooltip;

  /// Exposes `selected` to assistive tech when non-null.
  final bool? selected;

  final double pressedScale;
  final double hoverScale;

  /// Corner radius of [child] for the focus ring (ignored for circles).
  final double? focusRadius;
  final BoxShape focusShape;

  /// Minimum hit area; [child] is centred inside it.
  final Size minSize;

  final bool excludeChildSemantics;
  final FocusNode? focusNode;
  final bool autofocus;

  @override
  State<KararPressable> createState() => _KararPressableState();
}

class _KararPressableState extends State<KararPressable> {
  bool _pressed = false;
  bool _hovered = false;
  bool _focused = false;

  bool get _enabled => widget.onTap != null || widget.onLongPress != null;

  void _setPressed(bool value) {
    if (_pressed == value || !mounted) return;
    setState(() => _pressed = value);
  }

  void _activate() {
    widget.onTap?.call();
  }

  @override
  Widget build(BuildContext context) {
    final reduce = KararMotion.reduce(context);
    final scale = !_enabled || reduce
        ? 1.0
        : _pressed
            ? widget.pressedScale
            : _hovered
                ? widget.hoverScale
                : 1.0;
    final duration = _pressed ? KararMotion.press : KararMotion.release;
    final curve = _pressed ? KararMotion.standard : KararMotion.spring;

    Widget visual = CustomPaint(
      foregroundPainter: _focused
          ? KararFocusRingPainter(
              color: context.kc.primary,
              shape: widget.focusShape,
              radius: widget.focusRadius ?? 0,
            )
          : null,
      child: widget.child,
    );

    visual = AnimatedScale(
      scale: scale,
      duration: reduce ? Duration.zero : duration,
      curve: curve,
      child: visual,
    );

    Widget result = ConstrainedBox(
      constraints: BoxConstraints(
        minWidth: widget.minSize.width,
        minHeight: widget.minSize.height,
      ),
      child: Center(widthFactor: 1, heightFactor: 1, child: visual),
    );

    result = GestureDetector(
      behavior: HitTestBehavior.opaque,
      onTapDown: _enabled ? (_) => _setPressed(true) : null,
      onTapUp: _enabled ? (_) => _setPressed(false) : null,
      onTapCancel: _enabled ? () => _setPressed(false) : null,
      onTap: widget.onTap,
      onLongPress: widget.onLongPress,
      excludeFromSemantics: true,
      child: result,
    );

    result = FocusableActionDetector(
      enabled: _enabled,
      focusNode: widget.focusNode,
      autofocus: widget.autofocus,
      mouseCursor:
          _enabled ? SystemMouseCursors.click : SystemMouseCursors.basic,
      onShowFocusHighlight: (value) {
        if (_focused != value) setState(() => _focused = value);
      },
      onShowHoverHighlight: (value) {
        if (_hovered != value) setState(() => _hovered = value);
      },
      shortcuts: const {
        SingleActivator(LogicalKeyboardKey.enter): ActivateIntent(),
        SingleActivator(LogicalKeyboardKey.numpadEnter): ActivateIntent(),
        SingleActivator(LogicalKeyboardKey.space): ActivateIntent(),
      },
      actions: {
        ActivateIntent: CallbackAction<ActivateIntent>(
          onInvoke: (_) {
            _activate();
            return null;
          },
        ),
      },
      child: result,
    );

    final label = widget.semanticLabel ?? widget.tooltip;
    if (widget.tooltip != null) {
      result = Tooltip(
        message: widget.tooltip!,
        excludeFromSemantics: true,
        child: result,
      );
    }

    return Semantics(
      container: true,
      button: true,
      enabled: _enabled,
      selected: widget.selected,
      label: label,
      value: widget.semanticValue,
      hint: widget.semanticHint,
      onTap: widget.onTap,
      onLongPress: widget.onLongPress,
      excludeSemantics: widget.excludeChildSemantics && label != null,
      child: result,
    );
  }
}

/// Paints Karar's focus ring: [width] px stroke in [color], [gap] px outside
/// the child's bounds. Painted outside the box, so the parent must not clip.
class KararFocusRingPainter extends CustomPainter {
  const KararFocusRingPainter({
    required this.color,
    this.shape = BoxShape.rectangle,
    this.radius = 0,
    this.width = 3,
    this.gap = 3,
  });

  final Color color;
  final BoxShape shape;
  final double radius;
  final double width;
  final double gap;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = width;
    final inflate = gap + width / 2;
    final rect = (Offset.zero & size).inflate(inflate);
    if (shape == BoxShape.circle) {
      canvas.drawCircle(
        rect.center,
        size.shortestSide / 2 + inflate,
        paint,
      );
    } else {
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, Radius.circular(radius + inflate)),
        paint,
      );
    }
  }

  @override
  bool shouldRepaint(KararFocusRingPainter oldDelegate) =>
      oldDelegate.color != color ||
      oldDelegate.shape != shape ||
      oldDelegate.radius != radius ||
      oldDelegate.width != width ||
      oldDelegate.gap != gap;
}
