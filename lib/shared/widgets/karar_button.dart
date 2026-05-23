import 'package:flutter/material.dart';

enum KararButtonVariant { filled, outlined, text }

class KararButton extends StatelessWidget {
  const KararButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.variant = KararButtonVariant.filled,
    this.isLoading = false,
    this.icon,
    this.expand = true,
  });

  final String label;
  final VoidCallback? onPressed;
  final KararButtonVariant variant;
  final bool isLoading;
  final IconData? icon;
  final bool expand;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    
    final child = AnimatedSwitcher(
      duration: const Duration(milliseconds: 200),
      child: isLoading
          ? SizedBox(
              key: const ValueKey('loading'),
              width: 20,
              height: 20,
              child: CircularProgressIndicator(
                strokeWidth: 2,
                color: variant == KararButtonVariant.filled
                    ? Colors.white
                    : theme.colorScheme.primary,
              ),
            )
          : icon != null
              ? Row(
                  key: const ValueKey('content_icon'),
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(icon, size: 20),
                    const SizedBox(width: 10),
                    Text(label),
                  ],
                )
              : Text(label, key: const ValueKey('content_text')),
    );

    final effectiveOnPressed = isLoading ? null : onPressed;

    Widget button = switch (variant) {
      KararButtonVariant.filled => FilledButton(
          onPressed: effectiveOnPressed,
          child: child,
        ),
      KararButtonVariant.outlined => OutlinedButton(
          onPressed: effectiveOnPressed,
          child: child,
        ),
      KararButtonVariant.text => TextButton(
          onPressed: effectiveOnPressed,
          child: child,
        ),
    };

    if (expand) {
      button = SizedBox(width: double.infinity, child: button);
    }

    return button;
  }
}
