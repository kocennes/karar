import 'package:flutter/material.dart';

import '../../../core/theme/app_typography.dart';
import '../../../core/theme/karar_tokens.dart';

/// Minimal, dependency-free Markdown renderer for the bundled legal texts
/// (spec §6.10 `SimpleMarkdown`).
///
/// Supports what `assets/legal/*.md` uses: `#`/`##`/`###` headings,
/// paragraphs, `-`/`*` bullets (nested by indentation), `1.` ordered items,
/// `> ` quotes, fenced code blocks, `---` rules, pipe tables and the inline
/// `**bold**` and `` `code` `` spans. Everything renders with `Text.rich`, so
/// the app's font-size setting applies. Wrap it in a `SelectionArea` to make
/// the text selectable.
///
/// Lives in the legal feature (its only user).
/// TODO(owner D/shared): promote to lib/shared/widgets/simple_markdown.dart
/// if another area needs it.
class SimpleMarkdown extends StatelessWidget {
  const SimpleMarkdown({
    super.key,
    required this.data,
    this.padding = EdgeInsets.zero,
  });

  final String data;
  final EdgeInsets padding;

  @override
  Widget build(BuildContext context) {
    final blocks = parseMarkdownBlocks(data);
    return ListView.builder(
      padding: padding,
      itemCount: blocks.length,
      itemBuilder: (context, index) => _MdBlockView(
        block: blocks[index],
        isFirst: index == 0,
      ),
    );
  }
}

/// Block kinds produced by [parseMarkdownBlocks].
enum MdBlockType { heading, paragraph, bullet, ordered, quote, code, rule, table }

/// One parsed Markdown block.
@immutable
class MdBlock {
  const MdBlock(
    this.type, {
    this.text = '',
    this.level = 0,
    this.marker = '',
    this.rows = const [],
  });

  final MdBlockType type;
  final String text;

  /// Heading level (1–6) or list nesting depth (0-based).
  final int level;

  /// Ordered-list marker ("1.").
  final String marker;

  /// Table cells (first row = header).
  final List<List<String>> rows;
}

final _heading = RegExp(r'^(#{1,6})\s+(.*)$');
final _bullet = RegExp(r'^(\s*)[-*+]\s+(.*)$');
final _ordered = RegExp(r'^(\s*)(\d+[.)])\s+(.*)$');
final _rule = RegExp(r'^\s*([-*_])(\s*\1){2,}\s*$');
final _tableSeparator = RegExp(r'^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$');

/// Splits Markdown [source] into blocks.
List<MdBlock> parseMarkdownBlocks(String source) {
  final lines = source.replaceAll('\r\n', '\n').split('\n');
  final blocks = <MdBlock>[];
  final paragraph = <String>[];

  void flushParagraph() {
    if (paragraph.isEmpty) return;
    blocks.add(MdBlock(MdBlockType.paragraph, text: paragraph.join('\n')));
    paragraph.clear();
  }

  var i = 0;
  while (i < lines.length) {
    final line = lines[i];
    final trimmed = line.trim();

    if (trimmed.startsWith('```')) {
      flushParagraph();
      final code = <String>[];
      i++;
      while (i < lines.length && !lines[i].trim().startsWith('```')) {
        code.add(lines[i]);
        i++;
      }
      blocks.add(MdBlock(MdBlockType.code, text: code.join('\n')));
      i++; // closing fence
      continue;
    }

    if (trimmed.isEmpty) {
      flushParagraph();
      i++;
      continue;
    }

    final heading = _heading.firstMatch(trimmed);
    if (heading != null) {
      flushParagraph();
      blocks.add(MdBlock(
        MdBlockType.heading,
        level: heading.group(1)!.length,
        text: heading.group(2)!.trim(),
      ));
      i++;
      continue;
    }

    if (_rule.hasMatch(trimmed)) {
      flushParagraph();
      blocks.add(const MdBlock(MdBlockType.rule));
      i++;
      continue;
    }

    if (trimmed.startsWith('|')) {
      flushParagraph();
      final rows = <List<String>>[];
      while (i < lines.length && lines[i].trim().startsWith('|')) {
        final row = lines[i].trim();
        if (!_tableSeparator.hasMatch(row)) {
          var cells = row.split('|');
          if (cells.isNotEmpty && cells.first.trim().isEmpty) {
            cells = cells.sublist(1);
          }
          if (cells.isNotEmpty && cells.last.trim().isEmpty) {
            cells = cells.sublist(0, cells.length - 1);
          }
          rows.add(cells.map((c) => c.trim()).toList());
        }
        i++;
      }
      if (rows.isNotEmpty) {
        blocks.add(MdBlock(MdBlockType.table, rows: rows));
      }
      continue;
    }

    final bullet = _bullet.firstMatch(line);
    if (bullet != null) {
      flushParagraph();
      blocks.add(MdBlock(
        MdBlockType.bullet,
        level: (bullet.group(1)!.length / 2).floor().clamp(0, 4),
        text: bullet.group(2)!.trim(),
      ));
      i++;
      continue;
    }

    final ordered = _ordered.firstMatch(line);
    if (ordered != null) {
      flushParagraph();
      blocks.add(MdBlock(
        MdBlockType.ordered,
        level: (ordered.group(1)!.length / 2).floor().clamp(0, 4),
        marker: ordered.group(2)!,
        text: ordered.group(3)!.trim(),
      ));
      i++;
      continue;
    }

    if (trimmed.startsWith('>')) {
      flushParagraph();
      final quote = <String>[];
      while (i < lines.length && lines[i].trim().startsWith('>')) {
        quote.add(lines[i].trim().replaceFirst(RegExp(r'^>\s?'), ''));
        i++;
      }
      blocks.add(MdBlock(MdBlockType.quote, text: quote.join('\n')));
      continue;
    }

    paragraph.add(trimmed);
    i++;
  }
  flushParagraph();
  return blocks;
}

/// Inline `**bold**` and `` `code` `` spans.
List<InlineSpan> parseMarkdownInline(
  String text, {
  required TextStyle codeStyle,
  Color? codeBackground,
}) {
  final spans = <InlineSpan>[];
  final pattern = RegExp(r'(\*\*([^*]+)\*\*)|(`([^`]+)`)');
  var start = 0;
  for (final m in pattern.allMatches(text)) {
    if (m.start > start) {
      spans.add(TextSpan(text: text.substring(start, m.start)));
    }
    if (m.group(2) != null) {
      spans.add(TextSpan(
        text: m.group(2),
        style: const TextStyle(fontWeight: FontWeight.w800),
      ));
    } else {
      spans.add(TextSpan(
        text: m.group(4),
        style: codeStyle.copyWith(backgroundColor: codeBackground),
      ));
    }
    start = m.end;
  }
  if (start < text.length) spans.add(TextSpan(text: text.substring(start)));
  return spans;
}

class _MdBlockView extends StatelessWidget {
  const _MdBlockView({required this.block, required this.isFirst});

  final MdBlock block;
  final bool isFirst;

  static const _mono = TextStyle(
    fontFamily: 'monospace',
    fontFamilyFallback: ['Courier New', 'Courier'],
    fontSize: 14,
    height: 1.5,
  );

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final body = AppTypography.bodyL.copyWith(color: kc.text1);

    Widget rich(String text, TextStyle style) => Text.rich(
          TextSpan(
            style: style,
            children: parseMarkdownInline(
              text,
              codeStyle: _mono.copyWith(fontSize: 14, color: kc.text1),
              codeBackground: kc.muted,
            ),
          ),
        );

    switch (block.type) {
      case MdBlockType.heading:
        final (style, top) = switch (block.level) {
          1 => (AppTypography.titleXL, 0.0),
          2 => (AppTypography.headline, 24.0),
          _ => (AppTypography.tab, 16.0),
        };
        return Padding(
          padding: EdgeInsets.only(
            top: isFirst ? 0 : (block.level == 1 ? 28 : top),
            bottom: block.level == 1 ? 12 : 8,
          ),
          child: Semantics(
            header: true,
            child: rich(block.text, style.copyWith(color: kc.text1)),
          ),
        );

      case MdBlockType.paragraph:
        return Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: rich(block.text, body),
        );

      case MdBlockType.bullet:
      case MdBlockType.ordered:
        final ordered = block.type == MdBlockType.ordered;
        return Padding(
          padding: EdgeInsets.only(left: 8.0 + block.level * 18, bottom: 6),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              ExcludeSemantics(
                excluding: !ordered,
                child: SizedBox(
                  width: ordered ? 26 : 16,
                  child: Text(
                    ordered ? block.marker : '•',
                    style: body.copyWith(
                      fontWeight: FontWeight.w800,
                      color: ordered ? kc.text2 : kc.primary,
                    ),
                  ),
                ),
              ),
              const SizedBox(width: 4),
              Expanded(child: rich(block.text, body)),
            ],
          ),
        );

      case MdBlockType.quote:
        return Container(
          margin: const EdgeInsets.only(bottom: 12),
          padding: const EdgeInsets.fromLTRB(14, 8, 12, 8),
          decoration: BoxDecoration(
            border: Border(left: BorderSide(color: kc.primary, width: 3)),
            color: kc.primaryContainer.withValues(alpha: 0.5),
            borderRadius: const BorderRadius.horizontal(
              right: Radius.circular(KararRadius.sm),
            ),
          ),
          child: rich(block.text, body.copyWith(color: kc.text2)),
        );

      case MdBlockType.code:
        return Container(
          width: double.infinity,
          margin: const EdgeInsets.only(bottom: 14),
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: kc.muted,
            borderRadius: KararRadius.all(12),
          ),
          child: Text(block.text, style: _mono.copyWith(color: kc.text1)),
        );

      case MdBlockType.rule:
        return Padding(
          padding: const EdgeInsets.symmetric(vertical: 16),
          child: Divider(height: 1, color: kc.outlineVariant),
        );

      case MdBlockType.table:
        return _MdTable(rows: block.rows, rich: rich, body: body);
    }
  }
}

class _MdTable extends StatelessWidget {
  const _MdTable({required this.rows, required this.rich, required this.body});

  final List<List<String>> rows;
  final Widget Function(String text, TextStyle style) rich;
  final TextStyle body;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final columns = rows.fold<int>(0, (m, r) => r.length > m ? r.length : m);
    final cellStyle = AppTypography.bodyS.copyWith(color: kc.text1);

    TableRow row(List<String> cells, {bool header = false}) => TableRow(
          decoration: header ? BoxDecoration(color: kc.muted) : null,
          children: [
            for (var c = 0; c < columns; c++)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 260),
                  child: rich(
                    c < cells.length ? cells[c] : '',
                    header
                        ? cellStyle.copyWith(fontWeight: FontWeight.w800)
                        : cellStyle,
                  ),
                ),
              ),
          ],
        );

    return Container(
      margin: const EdgeInsets.only(bottom: 14),
      decoration: BoxDecoration(
        border: Border.all(color: kc.outlineVariant),
        borderRadius: KararRadius.all(12),
      ),
      clipBehavior: Clip.antiAlias,
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Table(
          defaultColumnWidth: const IntrinsicColumnWidth(),
          border: TableBorder(
            horizontalInside: BorderSide(color: kc.outlineVariant),
          ),
          children: [
            for (var r = 0; r < rows.length; r++) row(rows[r], header: r == 0),
          ],
        ),
      ),
    );
  }
}
