import 'package:flutter/widgets.dart';

/// Clips [child] to its own box vertically but lets it paint [bleed] px past
/// its left and right edges.
///
/// For lists of shadowed cards: a scroll viewport clips to its own bounds,
/// so the cards' glow / photo shadows (≈ 30 px wide) stop in a straight
/// vertical line on both sides of a narrow column. Wrap the list in this
/// widget and give the scroll view `clipBehavior: Clip.none`: content that
/// scrolls past the top or bottom is still cut at the list's edges, only
/// the sideways bleed is allowed.
class HorizontalBleedClip extends StatelessWidget {
  const HorizontalBleedClip({
    super.key,
    required this.child,
    this.bleed = 48,
  });

  final Widget child;

  /// How far (logical px) the child may paint left and right of its box.
  final double bleed;

  @override
  Widget build(BuildContext context) {
    return ClipRect(
      clipper: _HorizontalBleedClipper(bleed),
      child: child,
    );
  }
}

class _HorizontalBleedClipper extends CustomClipper<Rect> {
  const _HorizontalBleedClipper(this.bleed);

  final double bleed;

  @override
  Rect getClip(Size size) =>
      Rect.fromLTRB(-bleed, 0, size.width + bleed, size.height);

  @override
  bool shouldReclip(_HorizontalBleedClipper oldClipper) =>
      oldClipper.bleed != bleed;
}
