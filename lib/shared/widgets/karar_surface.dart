import 'package:flutter/material.dart';

import '../../core/theme/karar_tokens.dart';

/// Rounded surface card (spec §4.3): surface colour, radius 24, soft shadow
/// in light / 1 px hairline in dark, optional ink ripple.
///
/// Also used as a track for segmented controls:
/// `KararSurface(color: context.kc.muted, radius: KararRadius.pill,
///  padding: EdgeInsets.all(4), shadow: KararShadowKind.none, child: TabBar(…))`.
class KararSurface extends StatelessWidget {
  const KararSurface({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(20),
    this.radius = KararRadius.surface,
    this.color,
    this.shadow = KararShadowKind.soft,
    this.onTap,
    this.onLongPress,
    this.borderRadius,
    this.margin,
    this.border,
    this.clipBehavior = Clip.antiAlias,
    this.semanticLabel,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double radius;

  /// Defaults to `kc.surface`.
  final Color? color;
  final KararShadowKind shadow;
  final VoidCallback? onTap;
  final VoidCallback? onLongPress;

  /// Overrides [radius] (e.g. top-only corners).
  final BorderRadius? borderRadius;
  final EdgeInsetsGeometry? margin;

  /// Explicit border; overrides the dark-mode hairline.
  final BorderSide? border;
  final Clip clipBehavior;
  final String? semanticLabel;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    final br = borderRadius ?? BorderRadius.circular(radius);
    final hairline = KararShadow.hairlineFor(shadow, brightness);
    final side = border ??
        (hairline != null ? BorderSide(color: hairline) : BorderSide.none);
    final shadows = KararShadow.of(shadow, brightness);

    Widget content = Padding(padding: padding, child: child);
    if (onTap != null || onLongPress != null) {
      content = InkWell(
        onTap: onTap,
        onLongPress: onLongPress,
        borderRadius: br,
        child: content,
      );
    }

    Widget result = Material(
      color: color ?? kc.surface,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: br, side: side),
      clipBehavior: clipBehavior,
      child: content,
    );

    if (shadows.isNotEmpty) {
      result = DecoratedBox(
        decoration: BoxDecoration(borderRadius: br, boxShadow: shadows),
        child: result,
      );
    }

    if (semanticLabel != null) {
      result = Semantics(
        container: true,
        label: semanticLabel,
        button: onTap != null,
        child: result,
      );
    }

    if (margin != null) {
      result = Padding(padding: margin!, child: result);
    }
    return result;
  }
}
