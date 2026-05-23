import 'package:flutter/gestures.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

class MentionText extends StatelessWidget {
  const MentionText({
    super.key,
    required this.text,
    this.style,
  });

  final String text;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final mentionStyle = (style ?? theme.textTheme.bodyMedium)?.copyWith(
      color: theme.colorScheme.primary,
      fontWeight: FontWeight.bold,
    );

    final List<InlineSpan> spans = [];
    final pattern = RegExp(r'@(\w+)');
    int lastIndex = 0;

    for (final match in pattern.allMatches(text)) {
      if (match.start > lastIndex) {
        spans.add(TextSpan(text: text.substring(lastIndex, match.start)));
      }

      final username = match.group(1)!;
      spans.add(
        TextSpan(
          text: '@$username',
          style: mentionStyle,
          recognizer: TapGestureRecognizer()
            ..onTap = () => context.push('/users/$username'),
        ),
      );

      lastIndex = match.end;
    }

    if (lastIndex < text.length) {
      spans.add(TextSpan(text: text.substring(lastIndex)));
    }

    return RichText(
      text: TextSpan(
        style: style ?? theme.textTheme.bodyMedium,
        children: spans,
      ),
    );
  }
}
