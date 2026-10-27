import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../shared/models/post.dart';

/// Result split bar (spec §5.7).
///
/// * ≥ 40 votes: two rounded `Flexible` segments (Haklı | 3 px | Haksız,
///   same fills in both themes, no text inside), with the labels in a row
///   below ("✓ Haklı  %75" … "Haksız  %25 ✕") so the minority side never
///   truncates.
/// * < 40 votes: a muted track + "{n} oy · Karar için 40 oy bekleniyor".
/// * The split tweens from the previous fraction when the counts change
///   (compact 420 ms, detail 480 ms); the first appearance animates from
///   50/50 over 600 ms. Reduce motion: no animation.
class VoteBar extends StatefulWidget {
  const VoteBar({
    super.key,
    required this.post,
    this.isCompact = false,
    this.showLabels = true,
  });

  final Post post;
  final bool isCompact;

  /// Shows the label row below the bar (or the waiting text below 40 votes).
  final bool showLabels;

  @override
  State<VoteBar> createState() => _VoteBarState();
}

class _VoteBarState extends State<VoteBar> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: KararMotion.reveal,
  );
  late final CurvedAnimation _curve = CurvedAnimation(
    parent: _controller,
    curve: KararMotion.standard,
  );

  double _from = 0.5;
  double _to = 0.5;
  bool _started = false;

  double get _target => widget.post.hakliPercent / 100;

  double get _current => _from + (_to - _from) * _curve.value;

  @override
  void initState() {
    super.initState();
    _to = _target;
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    if (!widget.post.showPercentage || KararMotion.reduce(context)) {
      _from = _to;
      _controller.value = 1;
    } else {
      _from = 0.5;
      _controller.forward(from: 0);
    }
  }

  @override
  void didUpdateWidget(VoteBar oldWidget) {
    super.didUpdateWidget(oldWidget);
    final appeared =
        widget.post.showPercentage && !oldWidget.post.showPercentage;
    final target = _target;
    if (!appeared && target == _to) return;

    _from = appeared ? 0.5 : _current;
    _to = target;
    if (KararMotion.reduce(context) || !widget.post.showPercentage) {
      _from = _to;
      _controller.value = 1;
      return;
    }
    _controller.duration = appeared
        ? KararMotion.reveal
        : widget.isCompact
            ? KararMotion.split
            : const Duration(milliseconds: 480);
    _controller.forward(from: 0);
  }

  @override
  void dispose() {
    _curve.dispose();
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final post = widget.post;
    final kc = context.kc;
    final barHeight = widget.isCompact ? 10.0 : 14.0;

    if (!post.showPercentage) {
      return Semantics(
        container: true,
        label:
            '${post.totalVotes} oy kullanıldı. Karar için 40 oy bekleniyor.',
        child: ExcludeSemantics(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Container(
                height: barHeight,
                decoration: BoxDecoration(
                  color: kc.muted,
                  borderRadius: KararRadius.all(barHeight / 2),
                ),
              ),
              if (widget.showLabels) ...[
                const SizedBox(height: 8),
                Text(
                  '${post.totalVotes} oy · Karar için 40 oy bekleniyor',
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.labelS.copyWith(
                    color: kc.text2,
                    fontFeatures: AppTypography.tabular,
                  ),
                ),
              ],
            ],
          ),
        ),
      );
    }

    final hakliPercent = post.hakliPercent;
    final haksizPercent = 100 - hakliPercent;
    final labelStyle = AppTypography.label.copyWith(
      fontSize: widget.isCompact ? 13 : 14,
    );

    return Semantics(
      container: true,
      label:
          'Oy dağılımı: yüzde $hakliPercent haklı, yüzde $haksizPercent haksız. Toplam ${post.totalVotes} oy.',
      child: ExcludeSemantics(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SizedBox(
              height: barHeight,
              child: AnimatedBuilder(
                animation: _controller,
                builder: (context, _) {
                  final f = _current.clamp(0.0, 1.0);
                  // Stretch: the segments are childless boxes, so with the
                  // Row's default loose cross-axis they would collapse to 0.
                  return Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      if (hakliPercent > 0)
                        Flexible(
                          fit: FlexFit.tight,
                          flex: (f * 1000).round().clamp(1, 999),
                          child: _Segment(
                            color: kc.hakliFill,
                            height: barHeight,
                          ),
                        ),
                      if (hakliPercent > 0 && haksizPercent > 0)
                        const SizedBox(width: 3),
                      if (haksizPercent > 0)
                        Flexible(
                          fit: FlexFit.tight,
                          flex: ((1 - f) * 1000).round().clamp(1, 999),
                          child: _Segment(
                            color: kc.haksizFill,
                            height: barHeight,
                          ),
                        ),
                    ],
                  );
                },
              ),
            ),
            if (widget.showLabels) ...[
              const SizedBox(height: 8),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Flexible(
                    child: FittedBox(
                      fit: BoxFit.scaleDown,
                      alignment: Alignment.centerLeft,
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(
                            Icons.check_rounded,
                            size: 16,
                            color: kc.hakliText,
                          ),
                          const SizedBox(width: 4),
                          _SideLabel(
                            label: 'Haklı',
                            percent: hakliPercent,
                            style: labelStyle.copyWith(color: kc.hakliText),
                          ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Flexible(
                    child: FittedBox(
                      fit: BoxFit.scaleDown,
                      alignment: Alignment.centerRight,
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          _SideLabel(
                            label: 'Haksız',
                            percent: haksizPercent,
                            style: labelStyle.copyWith(color: kc.haksizText),
                          ),
                          const SizedBox(width: 4),
                          Icon(
                            Icons.close_rounded,
                            size: 16,
                            color: kc.haksizText,
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _Segment extends StatelessWidget {
  const _Segment({required this.color, required this.height});

  final Color color;
  final double height;

  @override
  Widget build(BuildContext context) {
    // Explicit size: a childless DecoratedBox takes the smallest size its
    // constraints allow, which is 0 × 0 under loose constraints.
    return SizedBox(
      height: height,
      width: double.infinity,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: color,
          borderRadius: KararRadius.all(height / 2),
        ),
      ),
    );
  }
}

/// "Haklı  %75" as one `Text.rich` (plain text stays `Haklı  %75`), with
/// tabular figures for the number.
class _SideLabel extends StatelessWidget {
  const _SideLabel({
    required this.label,
    required this.percent,
    required this.style,
  });

  final String label;
  final int percent;
  final TextStyle style;

  @override
  Widget build(BuildContext context) {
    return Text.rich(
      TextSpan(
        children: [
          TextSpan(text: label),
          const TextSpan(text: '  '),
          TextSpan(
            text: '%$percent',
            style: const TextStyle(fontFeatures: AppTypography.tabular),
          ),
        ],
      ),
      maxLines: 1,
      softWrap: false,
      style: style,
    );
  }
}
