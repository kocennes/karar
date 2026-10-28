import 'package:flutter/material.dart';
import 'package:shimmer/shimmer.dart';

import '../../core/theme/karar_tokens.dart';

class Skeleton extends StatelessWidget {
  const Skeleton({
    super.key,
    this.height,
    this.width,
    this.borderRadius = 8,
  });

  final double? height;
  final double? width;
  final double borderRadius;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;

    // Shimmer paints through the child's opaque pixels, so the child colour
    // only needs to be opaque; the visible tones are the theme's shimmer
    // tokens (lavender greys, never Material greys).
    return Shimmer.fromColors(
      baseColor: kc.shimmerBase,
      highlightColor: kc.shimmerHighlight,
      child: Container(
        height: height,
        width: width,
        decoration: BoxDecoration(
          color: kc.shimmerBase,
          borderRadius: BorderRadius.circular(borderRadius),
        ),
      ),
    );
  }
}

class PostCardSkeleton extends StatelessWidget {
  const PostCardSkeleton({super.key});

  @override
  Widget build(BuildContext context) {
    return const Card(
      child: Padding(
        padding: EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Skeleton(height: 16, width: 80),
                Spacer(),
                Skeleton(height: 12, width: 40),
              ],
            ),
            SizedBox(height: 12),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Skeleton(height: 20, width: double.infinity),
                      SizedBox(height: 8),
                      Skeleton(height: 20, width: 150),
                    ],
                  ),
                ),
                SizedBox(width: 12),
                Skeleton(height: 76, width: 76, borderRadius: 10),
              ],
            ),
            SizedBox(height: 14),
            Row(
              children: [
                Skeleton(height: 18, width: 40),
                SizedBox(width: 16),
                Skeleton(height: 18, width: 40),
                SizedBox(width: 16),
                Skeleton(height: 18, width: 40),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class PostDetailSkeleton extends StatelessWidget {
  const PostDetailSkeleton({super.key});

  @override
  Widget build(BuildContext context) {
    return const SingleChildScrollView(
      padding: EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Skeleton(height: 14, width: 120),
          SizedBox(height: 16),
          Skeleton(height: 28, width: double.infinity),
          SizedBox(height: 8),
          Skeleton(height: 28, width: 200),
          SizedBox(height: 24),
          Skeleton(height: 16, width: double.infinity),
          SizedBox(height: 8),
          Skeleton(height: 16, width: double.infinity),
          SizedBox(height: 8),
          Skeleton(height: 16, width: 180),
          SizedBox(height: 24),
          Skeleton(height: 200, width: double.infinity, borderRadius: 12),
          SizedBox(height: 24),
          Skeleton(height: 40, width: double.infinity),
          SizedBox(height: 16),
          Row(
            children: [
              Expanded(child: Skeleton(height: 56)),
              SizedBox(width: 12),
              Expanded(child: Skeleton(height: 56)),
            ],
          ),
        ],
      ),
    );
  }
}
