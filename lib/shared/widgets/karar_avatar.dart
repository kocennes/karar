import 'package:flutter/material.dart';

import '../../core/theme/karar_tokens.dart';
import '../../core/utils/tr_case.dart';

/// Avatar shape.
enum KararAvatarShape { circle, rounded }

/// Optional ring around the avatar.
enum KararAvatarRing {
  none,

  /// 2 px ring in the surface colour (identity pill, overlaps).
  surface,

  /// 3 px sweep gradient (#7D50F0 → #C2307E → #F5C04E) with a 4 px canvas
  /// gap — profile header.
  gradient,
}

/// User avatar with a coloured initial (spec §4.3).
///
/// [radius] is half the avatar size (ring and gap are added outside it).
/// Initials are upper-cased the Turkish way (`i` → `İ`). [anonymous] shows a
/// neutral person glyph; with [onColor] it is styled for gradient/photo
/// backgrounds (white 25 % + white icon).
class KararAvatar extends StatelessWidget {
  const KararAvatar({
    super.key,
    required this.username,
    this.radius = 18,
    this.fontSize,
    this.shape = KararAvatarShape.circle,
    this.ring = KararAvatarRing.none,
    this.anonymous = false,
    this.onColor = false,
  });

  final String username;
  final double radius;
  final double? fontSize;
  final KararAvatarShape shape;
  final KararAvatarRing ring;
  final bool anonymous;
  final bool onColor;

  static const _palette = [
    Color(0xFF6366F1), // Indigo
    Color(0xFF8B5CF6), // Violet
    Color(0xFFEC4899), // Pink
    Color(0xFFF43F5E), // Rose
    Color(0xFFF59E0B), // Amber
    Color(0xFF10B981), // Emerald
    Color(0xFF06B6D4), // Cyan
    Color(0xFF3B82F6), // Blue
  ];

  /// Deterministic background colour for [username].
  static Color colorFor(String username) {
    if (username.isEmpty) return _palette[0];
    final hash = username.codeUnits.reduce((a, b) => a + b);
    return _palette[hash % _palette.length];
  }

  /// Turkish-aware initial of [username] ("?" when empty).
  static String initialOf(String username) {
    final trimmed = username.trim();
    if (trimmed.isEmpty) return '?';
    final first = String.fromCharCode(trimmed.runes.first);
    return trUpper(first);
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final size = radius * 2;
    final ShapeBorder shapeBorder = shape == KararAvatarShape.circle
        ? const CircleBorder()
        : RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(size * 0.3),
          );

    final Color bg;
    final Widget content;
    if (anonymous) {
      bg = onColor ? Colors.white.withValues(alpha: 0.25) : kc.muted;
      content = Icon(
        Icons.person_rounded,
        size: radius * 1.15,
        color: onColor ? Colors.white : kc.text3,
      );
    } else {
      bg = colorFor(username);
      content = Text(
        initialOf(username),
        textScaler: TextScaler.noScaling,
        style: TextStyle(
          fontFamily: 'PlusJakartaSans',
          color: Colors.white,
          fontWeight: FontWeight.w800,
          fontSize: fontSize ?? (radius * 0.8),
          height: 1,
        ),
      );
    }

    Widget avatar = Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: ShapeDecoration(color: bg, shape: shapeBorder),
      child: content,
    );

    switch (ring) {
      case KararAvatarRing.none:
        break;
      case KararAvatarRing.surface:
        avatar = Container(
          padding: const EdgeInsets.all(2),
          decoration: ShapeDecoration(
            color: kc.surface,
            shape: _grow(shapeBorder, 2, size),
          ),
          child: avatar,
        );
      case KararAvatarRing.gradient:
        avatar = Container(
          padding: const EdgeInsets.all(3),
          decoration: ShapeDecoration(
            gradient: KararGradients.avatarRing,
            shape: _grow(shapeBorder, 7, size),
          ),
          child: Container(
            padding: const EdgeInsets.all(4),
            decoration: ShapeDecoration(
              color: kc.canvas,
              shape: _grow(shapeBorder, 4, size),
            ),
            child: avatar,
          ),
        );
    }
    return avatar;
  }

  /// Same shape grown by [by] px on each side (keeps concentric corners).
  static ShapeBorder _grow(ShapeBorder base, double by, double size) {
    if (base is RoundedRectangleBorder) {
      return RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(size * 0.3 + by),
      );
    }
    return const CircleBorder();
  }
}
