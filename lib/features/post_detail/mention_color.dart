import 'package:flutter/material.dart';

/// Paints the `@mentions` of a descendant `MentionText` in [color].
///
/// `MentionText` uses `colorScheme.primary` for mentions, which is #7D50F0
/// (4.49:1 on the canvas — just under AA for small text) and invisible on
/// gradient surfaces. Wrap it with `kc.primaryText` on surfaces and white on
/// colour.
class MentionColor extends StatelessWidget {
  const MentionColor({super.key, required this.color, required this.child});

  final Color color;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Theme(
      data: theme.copyWith(
        colorScheme: theme.colorScheme.copyWith(primary: color),
      ),
      child: child,
    );
  }
}
