import 'package:flutter/material.dart';

/// Type scale (spec §2.1). Plus Jakarta Sans is bundled as an asset family
/// (`PlusJakartaSans`, weights 400–800), so every `copyWith(fontWeight: …)`
/// gets a real face.
///
/// Sizes are at text scale 1.0; the app's font-size setting scales them via
/// `TextScaler`. Styles carry no colour: they inherit it from the
/// surrounding `DefaultTextStyle` / `copyWith(color: …)`.
abstract final class AppTypography {
  static const fontFamily = 'PlusJakartaSans';

  /// Tabular figures for numbers that change (counts, %).
  static const tabular = [FontFeature.tabularFigures()];

  /// Detail verdict word, karma totals — 32/36 w800.
  static const display = TextStyle(
    fontFamily: fontFamily,
    fontSize: 32,
    height: 36 / 32,
    fontWeight: FontWeight.w800,
    letterSpacing: -0.8,
  );

  /// Detail hero title, Keşfet title, auth headline — 26/31 w800.
  static const titleXL = TextStyle(
    fontFamily: fontFamily,
    fontSize: 26,
    height: 31 / 26,
    fontWeight: FontWeight.w800,
    letterSpacing: -0.6,
  );

  /// Tab-root app bar titles — 24/30 w800.
  static const screenTitle = TextStyle(
    fontFamily: fontFamily,
    fontSize: 24,
    height: 30 / 24,
    fontWeight: FontWeight.w800,
    letterSpacing: -0.5,
  );

  /// Feed card title (mobile) — 22/27 w800.
  static const cardTitle = TextStyle(
    fontFamily: fontFamily,
    fontSize: 22,
    height: 27 / 22,
    fontWeight: FontWeight.w800,
    letterSpacing: -0.4,
  );

  /// Feed card title on ≥ 1024 px — 24/29 w800.
  static const cardTitleDesktop = TextStyle(
    fontFamily: fontFamily,
    fontSize: 24,
    height: 29 / 24,
    fontWeight: FontWeight.w800,
    letterSpacing: -0.4,
  );

  /// Section titles, dialog titles — 20/26 w800.
  static const headline = TextStyle(
    fontFamily: fontFamily,
    fontSize: 20,
    height: 26 / 20,
    fontWeight: FontWeight.w800,
    letterSpacing: -0.3,
  );

  /// Pushed-page titles, sheet titles — 18/24 w800.
  static const appBarTitle = TextStyle(
    fontFamily: fontFamily,
    fontSize: 18,
    height: 24 / 18,
    fontWeight: FontWeight.w800,
    letterSpacing: -0.2,
  );

  /// Text tabs — 17/22, w800 active (use [tabInactive] for the 700 state).
  static const tab = TextStyle(
    fontFamily: fontFamily,
    fontSize: 17,
    height: 22 / 17,
    fontWeight: FontWeight.w800,
    letterSpacing: -0.2,
  );
  static const tabInactive = TextStyle(
    fontFamily: fontFamily,
    fontSize: 17,
    height: 22 / 17,
    fontWeight: FontWeight.w700,
    letterSpacing: -0.2,
  );

  /// Detail body text — 16/26 w400.
  static const bodyL = TextStyle(
    fontFamily: fontFamily,
    fontSize: 16,
    height: 26 / 16,
    fontWeight: FontWeight.w400,
  );

  /// Comments, descriptions — 15/22.5 w400.
  static const body = TextStyle(
    fontFamily: fontFamily,
    fontSize: 15,
    height: 1.5,
    fontWeight: FontWeight.w400,
  );

  /// List titles, names in rows — 15/21 w700.
  static const bodyStrong = TextStyle(
    fontFamily: fontFamily,
    fontSize: 15,
    height: 21 / 15,
    fontWeight: FontWeight.w700,
  );

  /// Excerpts, secondary lines — 14/20 w500.
  static const bodyS = TextStyle(
    fontFamily: fontFamily,
    fontSize: 14,
    height: 20 / 14,
    fontWeight: FontWeight.w500,
  );

  /// Medium buttons, selected chips — 14/18 w800.
  static const label = TextStyle(
    fontFamily: fontFamily,
    fontSize: 14,
    height: 18 / 14,
    fontWeight: FontWeight.w800,
  );

  /// Chips, small buttons, strong meta — 13/17 w700.
  static const labelS = TextStyle(
    fontFamily: fontFamily,
    fontSize: 13,
    height: 17 / 13,
    fontWeight: FontWeight.w700,
  );

  /// Timestamps, counts, captions — 12/16 w600.
  static const meta = TextStyle(
    fontFamily: fontFamily,
    fontSize: 12,
    height: 16 / 12,
    fontWeight: FontWeight.w600,
    letterSpacing: 0.1,
  );

  /// Badges, OP chip, count pills — 11/14 w800.
  static const micro = TextStyle(
    fontFamily: fontFamily,
    fontSize: 11,
    height: 14 / 11,
    fontWeight: FontWeight.w800,
    letterSpacing: 0.2,
  );

  /// 56 px CTAs — 16/20 w800.
  static const buttonL = TextStyle(
    fontFamily: fontFamily,
    fontSize: 16,
    height: 20 / 16,
    fontWeight: FontWeight.w800,
  );

  /// Material [TextTheme] mapping so untouched screens inherit the scale.
  static const TextTheme textTheme = TextTheme(
    displayLarge: TextStyle(
      fontFamily: fontFamily,
      fontSize: 40,
      height: 1.1,
      fontWeight: FontWeight.w800,
      letterSpacing: -1.0,
    ),
    displayMedium: TextStyle(
      fontFamily: fontFamily,
      fontSize: 32,
      height: 36 / 32,
      fontWeight: FontWeight.w800,
      letterSpacing: -0.8,
    ),
    displaySmall: TextStyle(
      fontFamily: fontFamily,
      fontSize: 28,
      height: 1.15,
      fontWeight: FontWeight.w800,
      letterSpacing: -0.6,
    ),
    headlineLarge: TextStyle(
      fontFamily: fontFamily,
      fontSize: 26,
      height: 31 / 26,
      fontWeight: FontWeight.w800,
      letterSpacing: -0.6,
    ),
    headlineMedium: TextStyle(
      fontFamily: fontFamily,
      fontSize: 24,
      height: 30 / 24,
      fontWeight: FontWeight.w800,
      letterSpacing: -0.5,
    ),
    headlineSmall: TextStyle(
      fontFamily: fontFamily,
      fontSize: 22,
      height: 27 / 22,
      fontWeight: FontWeight.w800,
      letterSpacing: -0.4,
    ),
    titleLarge: TextStyle(
      fontFamily: fontFamily,
      fontSize: 20,
      height: 26 / 20,
      fontWeight: FontWeight.w800,
      letterSpacing: -0.3,
    ),
    titleMedium: TextStyle(
      fontFamily: fontFamily,
      fontSize: 17,
      height: 22 / 17,
      fontWeight: FontWeight.w700,
      letterSpacing: -0.1,
    ),
    titleSmall: TextStyle(
      fontFamily: fontFamily,
      fontSize: 15,
      height: 21 / 15,
      fontWeight: FontWeight.w700,
      letterSpacing: 0,
    ),
    bodyLarge: TextStyle(
      fontFamily: fontFamily,
      fontSize: 16,
      height: 1.6,
      fontWeight: FontWeight.w400,
      letterSpacing: 0,
    ),
    bodyMedium: TextStyle(
      fontFamily: fontFamily,
      fontSize: 15,
      height: 1.5,
      fontWeight: FontWeight.w400,
      letterSpacing: 0,
    ),
    bodySmall: TextStyle(
      fontFamily: fontFamily,
      fontSize: 13,
      height: 1.45,
      fontWeight: FontWeight.w500,
      letterSpacing: 0,
    ),
    labelLarge: TextStyle(
      fontFamily: fontFamily,
      fontSize: 15,
      height: 20 / 15,
      fontWeight: FontWeight.w700,
      letterSpacing: 0,
    ),
    labelMedium: TextStyle(
      fontFamily: fontFamily,
      fontSize: 13,
      height: 17 / 13,
      fontWeight: FontWeight.w700,
      letterSpacing: 0,
    ),
    labelSmall: TextStyle(
      fontFamily: fontFamily,
      fontSize: 12,
      height: 16 / 12,
      fontWeight: FontWeight.w600,
      letterSpacing: 0.1,
    ),
  );

  // ---------------------------------------------------------------------
  // Legacy names (kept source-compatible; mapped onto the new scale).
  // ---------------------------------------------------------------------

  static const headlineLarge = screenTitle;
  static const headlineMedium = headline;

  /// Post title in compact lists.
  static const postTitle = TextStyle(
    fontFamily: fontFamily,
    fontSize: 16,
    height: 1.35,
    fontWeight: FontWeight.w800,
  );

  /// Post content in the detail screen.
  static const postContent = bodyL;

  /// Comment text.
  static const commentText = body;

  /// Meta text (time, category).
  static const metaText = meta;

  /// Vote count — bold, tabular.
  static const voteCount = TextStyle(
    fontFamily: fontFamily,
    fontSize: 18,
    fontWeight: FontWeight.w800,
    fontFeatures: tabular,
  );

  /// Vote percentage labels.
  static const votePercent = TextStyle(
    fontFamily: fontFamily,
    fontSize: 14,
    fontWeight: FontWeight.w800,
    fontFeatures: tabular,
  );

  /// Button label.
  static const buttonLabel = label;
}
