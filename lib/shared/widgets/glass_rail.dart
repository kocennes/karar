// The notch shape (RailNotchClipper) is adapted from `ClipStatusBar` in
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
// Changes: fixed pixel fillets instead of height fractions (the shape does
// not stretch with the rail height), mirrored so the attached edge is the
// right side, glass painting added.

import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';

import '../../core/theme/karar_tokens.dart';

/// Notched rail outline. Attached edge = right side: the shape flares into
/// the card edge at the top and bottom with [fillet]-px curves and has
/// [corner]-px rounded inner corners on the left.
class RailNotchClipper extends CustomClipper<Path> {
  const RailNotchClipper({this.fillet = 22, this.corner = 30});

  final double fillet;
  final double corner;

  /// The outline path for [size] (also used to paint the glass stroke).
  static Path pathFor(Size s, {double fillet = 22, double corner = 30}) {
    final w = s.width, h = s.height, f = fillet, c = corner;
    return Path()
      ..moveTo(w, h)
      ..quadraticBezierTo(w, h - f, 0.6 * w, h - f)
      ..lineTo(0.5 * w, h - f)
      ..quadraticBezierTo(0, h - f, 0, h - f - c)
      ..lineTo(0, f + c)
      ..quadraticBezierTo(0, f, 0.5 * w, f)
      ..lineTo(0.6 * w, f)
      ..quadraticBezierTo(w, f, w, 0)
      ..close();
  }

  @override
  Path getClip(Size size) => pathFor(size, fillet: fillet, corner: corner);

  @override
  bool shouldReclip(RailNotchClipper oldClipper) =>
      oldClipper.fillet != fillet || oldClipper.corner != corner;
}

/// Frosted vertical action rail glued to the right edge of a card
/// (spec §2.8 / §4.3).
///
/// * Gradient cards ([liveBlur] false): baked glass — white 16 % fill,
///   10 %→0 sheen, 1 px white 28 % stroke, no blur cost.
/// * Photos ([liveBlur] true): white 18 % + `BackdropFilter` blur σ 14
///   (grouped with the nearest `BackdropGroup`).
/// * High contrast: no translucency (black 45 % on photos, no fill on
///   gradients).
///
/// [child] is laid out from 34 px below the top, horizontally centred, and is
/// not clipped by the notch (capsule shadows may overflow).
class GlassRail extends StatelessWidget {
  const GlassRail({
    super.key,
    required this.child,
    this.width = 68,
    this.height = 384,
    this.liveBlur = false,
    this.fillet = 22,
    this.corner = 30,
    this.childTop = 34,
  });

  final Widget child;
  final double width;
  final double height;
  final bool liveBlur;
  final double fillet;
  final double corner;
  final double childTop;

  @override
  Widget build(BuildContext context) {
    final highContrast = MediaQuery.maybeHighContrastOf(context) ?? false;
    final clipper = RailNotchClipper(fillet: fillet, corner: corner);

    Widget glass;
    if (highContrast) {
      glass = liveBlur
          ? ClipPath(
              clipper: clipper,
              child: const ColoredBox(color: KararGlass.railFillHighContrast),
            )
          : const SizedBox.expand();
    } else {
      final paint = CustomPaint(
        painter: _GlassPainter(
          fill: liveBlur ? KararGlass.railFillPhoto : KararGlass.railFill,
          fillet: fillet,
          corner: corner,
        ),
        size: Size(width, height),
      );
      glass = liveBlur
          ? ClipPath(
              clipper: clipper,
              child: BackdropFilter.grouped(
                filter: ImageFilter.blur(
                  sigmaX: KararGlass.railBlur,
                  sigmaY: KararGlass.railBlur,
                ),
                child: paint,
              ),
            )
          : paint;
    }

    return SizedBox(
      width: width,
      height: height,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          Positioned.fill(child: ExcludeSemantics(child: glass)),
          Positioned(
            top: childTop,
            left: 0,
            right: 0,
            child: Center(child: child),
          ),
        ],
      ),
    );
  }
}

class _GlassPainter extends CustomPainter {
  const _GlassPainter({
    required this.fill,
    required this.fillet,
    required this.corner,
  });

  final Color fill;
  final double fillet;
  final double corner;

  @override
  void paint(Canvas canvas, Size size) {
    final path = RailNotchClipper.pathFor(size, fillet: fillet, corner: corner);
    canvas.drawPath(path, Paint()..color = fill);
    canvas.drawPath(
      path,
      Paint()
        ..shader = const LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [KararGlass.sheenTop, Color(0x00FFFFFF)],
        ).createShader(Offset.zero & size),
    );
    canvas.drawPath(
      path,
      Paint()
        ..color = KararGlass.stroke
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1,
    );
  }

  @override
  bool shouldRepaint(_GlassPainter oldDelegate) =>
      oldDelegate.fill != fill ||
      oldDelegate.fillet != fillet ||
      oldDelegate.corner != corner;
}
