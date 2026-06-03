import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';

class ShareCardWidget extends StatelessWidget {
  const ShareCardWidget({super.key, required this.post});

  final Post post;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      width: 360,
      height: 360,
      child: _CardBody(post: post),
    );
  }
}

class _CardBody extends StatelessWidget {
  const _CardBody({required this.post});

  final Post post;

  String _fmt(int n) {
    if (n >= 1000000) return '${(n / 1000000).toStringAsFixed(1)}M';
    if (n >= 1000) return '${(n / 1000).toStringAsFixed(1)}B';
    return n.toString();
  }

  @override
  Widget build(BuildContext context) {
    final hakliPct = post.hakliPercent;
    final showPct = post.showPercentage;

    return Container(
      decoration: const BoxDecoration(color: Color(0xFF0F0F1A)),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _Header(),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '"${post.title}"',
                    style: const TextStyle(
                      color: Colors.white,
                      fontSize: 18,
                      fontWeight: FontWeight.w600,
                      height: 1.4,
                    ),
                    maxLines: 5,
                    overflow: TextOverflow.ellipsis,
                  ),
                  const Spacer(),
                  _VoteBar(hakliPct: hakliPct, showPct: showPct),
                  const SizedBox(height: 10),
                  Row(
                    children: [
                      Text(
                        '✅ Haklı  ${_fmt(post.voteCountHakli)}',
                        style: const TextStyle(
                          color: AppColors.darkHakli,
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const Spacer(),
                      Text(
                        '❌ Haksız  ${_fmt(post.voteCountHaksiz)}',
                        style: const TextStyle(
                          color: AppColors.darkHaksiz,
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
          _Footer(postId: post.id),
        ],
      ),
    );
  }
}

class _Header extends StatelessWidget {
  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
      color: AppColors.primary,
      child: const Row(
        children: [
          Text('⚖️', style: TextStyle(fontSize: 20)),
          SizedBox(width: 8),
          Text(
            'karar',
            style: TextStyle(
              color: Colors.white,
              fontSize: 20,
              fontWeight: FontWeight.w900,
              letterSpacing: -0.5,
            ),
          ),
        ],
      ),
    );
  }
}

class _VoteBar extends StatelessWidget {
  const _VoteBar({required this.hakliPct, required this.showPct});

  final int hakliPct;
  final bool showPct;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        ClipRRect(
          borderRadius: BorderRadius.circular(4),
          child: SizedBox(
            height: 8,
            child: Row(
              children: [
                Expanded(
                  flex: hakliPct,
                  child: const ColoredBox(color: AppColors.darkHakli),
                ),
                Expanded(
                  flex: 100 - hakliPct,
                  child: const ColoredBox(color: AppColors.darkHaksiz),
                ),
              ],
            ),
          ),
        ),
        if (showPct) ...[
          const SizedBox(height: 4),
          Text(
            '%$hakliPct Haklı',
            style: const TextStyle(
              color: AppColors.darkHakli,
              fontSize: 12,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ],
    );
  }
}

class _Footer extends StatelessWidget {
  const _Footer({required this.postId});

  final String postId;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
      decoration: const BoxDecoration(
        border: Border(top: BorderSide(color: Color(0xFF2A2A3A))),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Sen ne düşünüyorsun?',
            style: TextStyle(
              color: Colors.white70,
              fontSize: 11,
              fontWeight: FontWeight.w500,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            'karar.app/posts/$postId',
            style: TextStyle(
              color: AppColors.primary.withValues(alpha: 0.8),
              fontSize: 11,
            ),
          ),
        ],
      ),
    );
  }
}

Future<List<int>?> captureShareCard(GlobalKey repaintKey) async {
  final boundary =
      repaintKey.currentContext?.findRenderObject() as RenderRepaintBoundary?;
  if (boundary == null) return null;
  final image = await boundary.toImage(pixelRatio: 3.0);
  final byteData = await image.toByteData(format: ui.ImageByteFormat.png);
  return byteData?.buffer.asUint8List().toList();
}
