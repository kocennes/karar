import 'package:flutter/material.dart';

class HighlightText extends StatelessWidget {
  const HighlightText({
    super.key,
    required this.text,
    required this.highlight,
    this.style,
    this.highlightStyle,
    this.maxLines,
    this.overflow,
  });

  final String text;
  final String highlight;
  final TextStyle? style;
  final TextStyle? highlightStyle;
  final int? maxLines;
  final TextOverflow? overflow;

  @override
  Widget build(BuildContext context) {
    if (highlight.isEmpty || !text.toLowerCase().contains(highlight.toLowerCase())) {
      return Text(
        text,
        style: style,
        maxLines: maxLines,
        overflow: overflow,
      );
    }

    final children = <TextSpan>[];
    final lowerText = text.toLowerCase();
    final lowerHighlight = highlight.toLowerCase();
    int start = 0;
    int index = lowerText.indexOf(lowerHighlight);

    while (index != -1) {
      if (index > start) {
        children.add(TextSpan(text: text.substring(start, index)));
      }
      children.add(TextSpan(
        text: text.substring(index, index + highlight.length),
        style: highlightStyle ??
            TextStyle(
              backgroundColor: Theme.of(context).colorScheme.primary.withValues(alpha: 0.2),
              fontWeight: FontWeight.bold,
            ),
      ));
      start = index + highlight.length;
      index = lowerText.indexOf(lowerHighlight, start);
    }

    if (start < text.length) {
      children.add(TextSpan(text: text.substring(start)));
    }

    return RichText(
      maxLines: maxLines,
      overflow: overflow ?? TextOverflow.clip,
      text: TextSpan(
        style: style ?? DefaultTextStyle.of(context).style,
        children: children,
      ),
    );
  }
}
