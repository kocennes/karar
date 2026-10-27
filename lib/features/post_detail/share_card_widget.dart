import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_logo.dart';
import '../../shared/widgets/on_color_chip.dart';
import '../../shared/widgets/post_surface.dart';
import '../../shared/widgets/verdict_chip.dart';

/// Square share card (captured to PNG): a mini feature card — category
/// gradient, big white quoted title and the verdict chip (spec §6.12).
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

  @override
  Widget build(BuildContext context) {
    final style = CategoryStyle.of(post.category);
    final white = context.kc.onColor;
    return MediaQuery.withNoTextScaling(
      child: PostSurface(
        post: post,
        showPhoto: false,
        showRailSheen: false,
        radius: KararRadius.card,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(22, 20, 22, 20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  KararLogo(
                    size: LogoSize.small,
                    wordmarkColor: white,
                  ),
                  const Spacer(),
                  Flexible(
                    child: OnColorChip(
                      icon: style.icon,
                      label: post.category.name,
                    ),
                  ),
                ],
              ),
              const Spacer(),
              Text(
                '“${post.title}”',
                maxLines: 5,
                overflow: TextOverflow.ellipsis,
                style: AppTypography.cardTitle.copyWith(color: white),
              ),
              const SizedBox(height: 16),
              VerdictChip(post: post),
              const SizedBox(height: 14),
              Row(
                children: [
                  Text(
                    'Sen ne düşünüyorsun?',
                    style: AppTypography.meta.copyWith(
                      color: white,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      'karar.app/posts/${post.id}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      textAlign: TextAlign.end,
                      style: AppTypography.meta.copyWith(color: white),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
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
