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
/// the light stop of every palette is ≥ 4.9:1 in both themes.
///
/// Dark theme ([gradientFor] with `Brightness.dark`): the saturated light
/// stops are pulled ~12 % towards the canvas (#0D0C12) so large violet /
/// magenta / blue slabs do not vibrate on the near-black page, the Amber
/// bottom stop is lifted (it read brown and muddy) and the Ink ("Diğer")
/// palette is lifted so the card does not dissolve into the canvas.
@immutable
class CategoryStyle {
  const CategoryStyle._({
    required this.start,
    required this.end,
    required this.icon,
    required this.iconOutlined,
    Color? darkStart,
    Color? darkEnd,
    this.lowKey = false,
  })  : _darkStart = darkStart,
        _darkEnd = darkEnd;

  /// Light (top-left) stop.
  final Color start;

  /// Dark (bottom-right) stop.
  final Color end;

  /// Filled icon (selected / on colour).
  final IconData icon;

  /// Outlined icon (unselected).
  final IconData iconOutlined;

  final Color? _darkStart;
  final Color? _darkEnd;

  /// A low-key palette (Ink / "Diğer") whose bottom stop sits close to the
  /// dark canvas: dark cards get a stronger hairline.
  final bool lowKey;

  /// Light stop for [brightness].
  Color startFor(Brightness brightness) =>
      brightness == Brightness.dark ? (_darkStart ?? start) : start;

  /// Dark stop for [brightness].
  Color endFor(Brightness brightness) =>
      brightness == Brightness.dark ? (_darkEnd ?? end) : end;

  /// Card / tile background, top-left → bottom-right (light-theme stops).
  LinearGradient get gradient => LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [start, end],
      );

  /// Darkened "seen" variant: both stops lerped 30 % towards #15131F
  /// (still AA with white text, reads as visited). Light-theme stops.
  LinearGradient get seenGradient => LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [_seen(start, Brightness.light), _seen(end, Brightness.light)],
      );

  /// Gradient for the given state and theme. In dark, "seen" lerps towards
  /// #26233A (a raised dark tone) instead of #15131F, so visited cards
  /// stay a visible step above the canvas instead of turning muddy.
  LinearGradient gradientFor({
    bool seen = false,
    Brightness brightness = Brightness.light,
  }) {
    if (brightness == Brightness.light) return seen ? seenGradient : gradient;
    final s = startFor(brightness);
    final e = endFor(brightness);
    return LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: seen ? [_seen(s, brightness), _seen(e, brightness)] : [s, e],
    );
  }

  /// Card glow (light theme shadow): dark stop at 55 % alpha.
  Color get glow => end.withValues(alpha: 0.55);

  static Color _seen(Color c, Brightness b) => Color.lerp(
        c,
        b == Brightness.dark ? const Color(0xFF26233A) : const Color(0xFF15131F),
        0.30,
      )!;

  // -- Palettes -------------------------------------------------------------
  //
  // Dark stops: start = lerp(start, #0D0C12, .12), end = lerp(end, #0D0C12,
  // .06). White on the dark light stop: violet 5.88, magenta 6.26, ocean
  // 5.99, amber 5.02, ink 6.67 (seen: 7.92 / 8.50 / 8.09 / 7.18 / 8.61).

  static const violet = CategoryStyle._(
    start: Color(0xFF7D50F0),
    end: Color(0xFF3F1A9E),
    darkStart: Color(0xFF7048D5),
    darkEnd: Color(0xFF3C1996),
    icon: Icons.work_rounded,
    iconOutlined: Icons.work_outline_rounded,
  );
  static const magenta = CategoryStyle._(
    start: Color(0xFFC2307E),
    end: Color(0xFF6D1149),
    darkStart: Color(0xFFAC2C71),
    darkEnd: Color(0xFF671146),
    icon: Icons.favorite_rounded,
    iconOutlined: Icons.favorite_border_rounded,
  );
  static const ocean = CategoryStyle._(
    start: Color(0xFF2563EB),
    end: Color(0xFF172F7A),
    darkStart: Color(0xFF235AD5),
    darkEnd: Color(0xFF162D74),
    icon: Icons.home_rounded,
    iconOutlined: Icons.home_outlined,
  );
  static const amber = CategoryStyle._(
    start: Color(0xFFB45309),
    end: Color(0xFF6B2A0B),
    darkStart: Color(0xFFB45309),
    darkEnd: Color(0xFF8A3412),
    icon: Icons.groups_rounded,
    iconOutlined: Icons.groups_outlined,
  );
  static const ink = CategoryStyle._(
    start: Color(0xFF4A4766),
    end: Color(0xFF15131F),
    darkStart: Color(0xFF5B5785),
    darkEnd: Color(0xFF2E2A47),
    icon: Icons.more_horiz_rounded,
    iconOutlined: Icons.more_horiz_rounded,
    lowKey: true,
  );

  /// "Hepsi" (id 0).
  static const all = CategoryStyle._(
    start: Color(0xFF7D50F0),
    end: Color(0xFF3F1A9E),
    darkStart: Color(0xFF7048D5),
    darkEnd: Color(0xFF3C1996),
    icon: Icons.apps_rounded,
    iconOutlined: Icons.apps_outlined,
  );

  /// Unknown category (violet with a generic label icon).
  static const unknown = CategoryStyle._(
    start: Color(0xFF7D50F0),
    end: Color(0xFF3F1A9E),
    darkStart: Color(0xFF7048D5),
    darkEnd: Color(0xFF3C1996),
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
        darkStart: base._darkStart,
        darkEnd: base._darkEnd,
        lowKey: base.lowKey,
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
