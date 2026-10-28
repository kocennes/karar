import 'package:flutter/material.dart';

import 'karar_tokens.dart';

/// Rounded box border for Karar's filled text fields (spec §3, inputs).
///
/// It paints like an [OutlineInputBorder] (a 1–2 px stroke around a
/// rounded rectangle) but reports [isOutline] `false`, so [InputDecorator]
/// lays the field out as a Material 3 *filled* field: the floating label
/// sits inside the fill, above the typed text, instead of straddling the
/// top edge on a notch. With an outline border on a filled field the label
/// sat half on the canvas and half on the fill (very visible in dark mode,
/// where the fill differs from the canvas), and the notch looked like a
/// broken box.
///
/// The border never opens a gap for the label, so the `gap*` arguments of
/// [paint] are ignored.
@immutable
class KararFieldBorder extends InputBorder {
  const KararFieldBorder({
    super.borderSide = const BorderSide(),
    this.borderRadius =
        const BorderRadius.all(Radius.circular(KararRadius.input)),
  });

  /// Corner radii of the field.
  final BorderRadius borderRadius;

  @override
  bool get isOutline => false;

  @override
  bool get preferPaintInterior => true;

  @override
  KararFieldBorder copyWith({
    BorderSide? borderSide,
    BorderRadius? borderRadius,
  }) {
    return KararFieldBorder(
      borderSide: borderSide ?? this.borderSide,
      borderRadius: borderRadius ?? this.borderRadius,
    );
  }

  @override
  EdgeInsetsGeometry get dimensions => EdgeInsets.all(borderSide.width);

  @override
  KararFieldBorder scale(double t) {
    return KararFieldBorder(
      borderSide: borderSide.scale(t),
      borderRadius: borderRadius * t,
    );
  }

  @override
  ShapeBorder? lerpFrom(ShapeBorder? a, double t) {
    if (a is KararFieldBorder) {
      return KararFieldBorder(
        borderSide: BorderSide.lerp(a.borderSide, borderSide, t),
        borderRadius: BorderRadius.lerp(a.borderRadius, borderRadius, t)!,
      );
    }
    return super.lerpFrom(a, t);
  }

  @override
  ShapeBorder? lerpTo(ShapeBorder? b, double t) {
    if (b is KararFieldBorder) {
      return KararFieldBorder(
        borderSide: BorderSide.lerp(borderSide, b.borderSide, t),
        borderRadius: BorderRadius.lerp(borderRadius, b.borderRadius, t)!,
      );
    }
    return super.lerpTo(b, t);
  }

  RRect _outer(Rect rect, TextDirection? textDirection) =>
      borderRadius.resolve(textDirection).toRRect(rect);

  @override
  Path getInnerPath(Rect rect, {TextDirection? textDirection}) {
    return Path()
      ..addRRect(_outer(rect, textDirection).deflate(borderSide.width));
  }

  @override
  Path getOuterPath(Rect rect, {TextDirection? textDirection}) {
    return Path()..addRRect(_outer(rect, textDirection));
  }

  @override
  void paintInterior(
    Canvas canvas,
    Rect rect,
    Paint paint, {
    TextDirection? textDirection,
  }) {
    canvas.drawRRect(_outer(rect, textDirection), paint);
  }

  @override
  void paint(
    Canvas canvas,
    Rect rect, {
    double? gapStart,
    double gapExtent = 0.0,
    double gapPercentage = 0.0,
    TextDirection? textDirection,
  }) {
    if (borderSide.style == BorderStyle.none) return;
    canvas.drawRRect(
      _outer(rect, textDirection).deflate(borderSide.width / 2),
      borderSide.toPaint(),
    );
  }

  @override
  bool operator ==(Object other) {
    if (identical(this, other)) return true;
    if (other.runtimeType != runtimeType) return false;
    return other is KararFieldBorder &&
        other.borderSide == borderSide &&
        other.borderRadius == borderRadius;
  }

  @override
  int get hashCode => Object.hash(borderSide, borderRadius);
}
