import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

/// Motion tokens (spec §2.9).
///
/// Reduce motion: when `MediaQuery.disableAnimationsOf(context)` is true,
/// every duration becomes zero ([d]) except opacity fades ([fade] → 120 ms);
/// no pulse rings, no count-ups, no confetti ([reduce]).
abstract final class KararMotion {
  static const press = Duration(milliseconds: 90);
  static const fast = Duration(milliseconds: 150);
  static const base = Duration(milliseconds: 220);
  static const nav = Duration(milliseconds: 280);
  static const sheet = Duration(milliseconds: 320);
  static const split = Duration(milliseconds: 420);
  static const reveal = Duration(milliseconds: 600);
  static const pulse = Duration(milliseconds: 1200);

  /// Release after a press.
  static const release = Duration(milliseconds: 280);

  /// Opacity fades under reduce-motion.
  static const reducedFade = Duration(milliseconds: 120);

  static const Curve standard = Curves.easeOutCubic;
  static const Curve emphasized = Cubic(0.2, 0, 0, 1);

  /// ≈ 4 % overshoot. Do not use `Curves.elasticOut`.
  static const Curve spring = Cubic(0.34, 1.36, 0.64, 1);
  static const Curve exit = Curves.easeInCubic;

  /// Whether the user asked the platform to reduce motion.
  static bool reduce(BuildContext context) =>
      MediaQuery.maybeDisableAnimationsOf(context) ?? false;

  /// [duration], or zero under reduce-motion.
  static Duration d(BuildContext context, Duration duration) =>
      reduce(context) ? Duration.zero : duration;

  /// Duration for an opacity fade: [duration], or 120 ms under reduce-motion.
  static Duration fade(BuildContext context, Duration duration) =>
      reduce(context) ? reducedFade : duration;
}

/// Haptics helper (spec §2.9). Do not add a UI haptic for votes on the
/// detail screen: `PostDetailNotifier` already vibrates on native.
abstract final class KararHaptics {
  /// A vote was cast.
  static Future<void> vote() => HapticFeedback.mediumImpact();

  /// A vote was removed.
  static Future<void> unvote() => HapticFeedback.lightImpact();

  /// Save, tab, chip and other toggles.
  static Future<void> toggle() => HapticFeedback.selectionClick();

  /// Long-press menus.
  static Future<void> longPress() => HapticFeedback.mediumImpact();

  /// Light tap (create notch).
  static Future<void> tap() => HapticFeedback.lightImpact();
}
