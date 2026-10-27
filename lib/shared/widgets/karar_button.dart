import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';

/// Button variants.
///
/// * [filled] — brand pill (`primaryFill`, white), light-theme glow.
/// * [outlined] — 1.5 px outline pill, text1.
/// * [text] — `primaryText` text button.
/// * [tonal] — `primaryContainer` / `onPrimaryContainer`.
/// * [ink] — `ink` / `onInk` (inverse in dark).
/// * [danger] — `haksizFill` / white (destructive confirms).
enum KararButtonVariant { filled, outlined, text, tonal, ink, danger }

/// Button heights: medium 52 (`label` 14/800), large 56 (`buttonL` 16/800).
enum KararButtonSize { medium, large }

class KararButton extends StatelessWidget {
  const KararButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.variant = KararButtonVariant.filled,
    this.isLoading = false,
    this.icon,
    this.expand = true,
    this.size = KararButtonSize.medium,
  });

  final String label;
  final VoidCallback? onPressed;
  final KararButtonVariant variant;
  final bool isLoading;
  final IconData? icon;
  final bool expand;
  final KararButtonSize size;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;
    final height = size == KararButtonSize.large ? 56.0 : 52.0;
    final textStyle = size == KararButtonSize.large
        ? AppTypography.buttonL
        : AppTypography.label;

    final (Color? bg, Color fg) = switch (variant) {
      KararButtonVariant.filled => (kc.primaryFill, Colors.white),
      KararButtonVariant.outlined => (null, kc.text1),
      KararButtonVariant.text => (null, kc.primaryText),
      KararButtonVariant.tonal => (kc.primaryContainer, kc.onPrimaryContainer),
      KararButtonVariant.ink => (kc.ink, kc.onInk),
      KararButtonVariant.danger => (kc.haksizFill, Colors.white),
    };

    final child = AnimatedSwitcher(
      duration: const Duration(milliseconds: 200),
      child: isLoading
          ? SizedBox(
              key: const ValueKey('loading'),
              width: 20,
              height: 20,
              child: CircularProgressIndicator(
                strokeWidth: 2,
                color: bg != null ? fg : kc.primary,
              ),
            )
          : icon != null
              ? Row(
                  key: const ValueKey('content_icon'),
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(icon, size: 20),
                    const SizedBox(width: 8),
                    Flexible(
                      child: Text(
                        label,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                  ],
                )
              : Text(
                  label,
                  key: const ValueKey('content_text'),
                  maxLines: 2,
                  textAlign: TextAlign.center,
                  overflow: TextOverflow.ellipsis,
                ),
    );

    final effectiveOnPressed = isLoading ? null : onPressed;
    final sizing = ButtonStyle(
      minimumSize: WidgetStatePropertyAll(Size(64, height)),
      textStyle: WidgetStatePropertyAll(textStyle),
    );

    Widget button = switch (variant) {
      KararButtonVariant.filled => FilledButton(
          onPressed: effectiveOnPressed,
          style: sizing,
          child: child,
        ),
      KararButtonVariant.outlined => OutlinedButton(
          onPressed: effectiveOnPressed,
          style: sizing,
          child: child,
        ),
      KararButtonVariant.text => TextButton(
          onPressed: effectiveOnPressed,
          style: sizing.copyWith(
            padding: const WidgetStatePropertyAll(
              EdgeInsets.symmetric(horizontal: 16),
            ),
          ),
          child: child,
        ),
      KararButtonVariant.tonal ||
      KararButtonVariant.ink ||
      KararButtonVariant.danger =>
        FilledButton(
          onPressed: effectiveOnPressed,
          style: sizing.copyWith(
            backgroundColor: WidgetStateProperty.resolveWith(
              (states) =>
                  states.contains(WidgetState.disabled) ? kc.muted : bg,
            ),
            foregroundColor: WidgetStateProperty.resolveWith(
              (states) =>
                  states.contains(WidgetState.disabled) ? kc.text3 : fg,
            ),
            iconColor: WidgetStateProperty.resolveWith(
              (states) =>
                  states.contains(WidgetState.disabled) ? kc.text3 : fg,
            ),
            overlayColor: WidgetStateProperty.resolveWith(
              (states) => states.contains(WidgetState.pressed)
                  ? fg.withValues(alpha: 0.10)
                  : states.contains(WidgetState.hovered) ||
                          states.contains(WidgetState.focused)
                      ? fg.withValues(alpha: 0.06)
                      : null,
            ),
          ),
          child: child,
        ),
    };

    final glow =
        variant == KararButtonVariant.filled && effectiveOnPressed != null
            ? KararShadow.primaryButton(brightness)
            : const <BoxShadow>[];
    if (glow.isNotEmpty) {
      button = DecoratedBox(
        decoration: ShapeDecoration(
          shape: const StadiumBorder(),
          shadows: glow,
        ),
        child: button,
      );
    }

    if (expand) {
      button = SizedBox(width: double.infinity, child: button);
    }

    return button;
  }
}
