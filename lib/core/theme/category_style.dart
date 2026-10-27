import 'package:flutter/material.dart';

import '../../shared/models/post.dart';
import '../utils/tr_case.dart';

/// Category palettes and icons (spec §2.3).
///
/// Map by id, fall back by Turkish-normalised name, then by `id % 5`.
/// **Never render `Category.icon` as text** — the backend stores short words
/// there ("Aşk", "İş"), which produced labels like "Aşk İlişkiler".
///
/// Hues deliberately avoid pure green and red (vote semantics). White text on
/// the light stop of every palette is ≥ 4.9:1.
@immutable
class CategoryStyle {
  const CategoryStyle._({
    required this.start,
    required this.end,
    required this.icon,
    required this.iconOutlined,
  });

  /// Light (top-left) stop.
  final Color start;

  /// Dark (bottom-right) stop.
  final Color end;

  /// Filled icon (selected / on colour).
  final IconData icon;

  /// Outlined icon (unselected).
  final IconData iconOutlined;

  /// Card / tile background, top-left → bottom-right.
  LinearGradient get gradient => LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [start, end],
      );

  /// Darkened "seen" variant: both stops lerped 30 % towards #15131F
  /// (still AA with white text, reads as visited).
  LinearGradient get seenGradient => LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [_seen(start), _seen(end)],
      );

  /// Gradient for the given state.
  LinearGradient gradientFor({bool seen = false}) =>
      seen ? seenGradient : gradient;

  /// Card glow (light theme shadow): dark stop at 55 % alpha.
  Color get glow => end.withValues(alpha: 0.55);

  static Color _seen(Color c) => Color.lerp(c, const Color(0xFF15131F), 0.30)!;

  // -- Palettes -------------------------------------------------------------

  static const violet = CategoryStyle._(
    start: Color(0xFF7D50F0),
    end: Color(0xFF3F1A9E),
    icon: Icons.work_rounded,
    iconOutlined: Icons.work_outline_rounded,
  );
  static const magenta = CategoryStyle._(
    start: Color(0xFFC2307E),
    end: Color(0xFF6D1149),
    icon: Icons.favorite_rounded,
    iconOutlined: Icons.favorite_border_rounded,
  );
  static const ocean = CategoryStyle._(
    start: Color(0xFF2563EB),
    end: Color(0xFF172F7A),
    icon: Icons.home_rounded,
    iconOutlined: Icons.home_outlined,
  );
  static const amber = CategoryStyle._(
    start: Color(0xFFB45309),
    end: Color(0xFF6B2A0B),
    icon: Icons.groups_rounded,
    iconOutlined: Icons.groups_outlined,
  );
  static const ink = CategoryStyle._(
    start: Color(0xFF4A4766),
    end: Color(0xFF15131F),
    icon: Icons.more_horiz_rounded,
    iconOutlined: Icons.more_horiz_rounded,
  );

  /// "Hepsi" (id 0).
  static const all = CategoryStyle._(
    start: Color(0xFF7D50F0),
    end: Color(0xFF3F1A9E),
    icon: Icons.apps_rounded,
    iconOutlined: Icons.apps_outlined,
  );

  /// Unknown category (violet with a generic label icon).
  static const unknown = CategoryStyle._(
    start: Color(0xFF7D50F0),
    end: Color(0xFF3F1A9E),
    icon: Icons.label_rounded,
    iconOutlined: Icons.label_outline_rounded,
  );

  static const _byId = <int, CategoryStyle>{
    0: all,
    1: violet,
    2: magenta,
    3: ocean,
    4: amber,
    5: ink,
  };

  static const _byName = <String, int>{
    'hepsi': 0,
    'tumu': 0,
    'is hayati': 1,
    'is': 1,
    'iliskiler': 2,
    'iliski': 2,
    'ask': 2,
    'aile': 3,
    'ev': 3,
    'arkadaslik': 4,
    'dost': 4,
    'dostluk': 4,
    'diger': 5,
  };

  static const _cycle = [violet, magenta, ocean, amber, ink];

  /// Style for a category: by id, then by name, then by `id % 5`.
  static CategoryStyle of(Category category) =>
      resolve(id: category.id, name: category.name);

  /// Same lookup as [of] from raw values.
  static CategoryStyle resolve({int? id, String? name}) {
    if (id != null) {
      final byId = _byId[id];
      if (byId != null) return byId;
    }
    if (name != null && name.trim().isNotEmpty) {
      final key = _byName[trFold(name)];
      if (key != null) return _byId[key]!;
    }
    if (id != null) {
      final base = _cycle[id.abs() % _cycle.length];
      return CategoryStyle._(
        start: base.start,
        end: base.end,
        icon: unknown.icon,
        iconOutlined: unknown.iconOutlined,
      );
    }
    return unknown;
  }

  /// Palette by index (rank tiles, onboarding steps): cycles through the five
  /// palettes.
  static CategoryStyle cycle(int index) => _cycle[index.abs() % _cycle.length];
}
