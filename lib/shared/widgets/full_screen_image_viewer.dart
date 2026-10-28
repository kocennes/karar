import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../../core/theme/karar_tokens.dart';

/// Full-screen photo viewer: a black media backdrop in both themes with
/// white controls (`kc.mediaBackdrop` / `kc.onColor`).
class FullScreenImageViewer extends StatelessWidget {
  const FullScreenImageViewer({
    super.key,
    required this.imageUrl,
    this.heroTag,
  });

  final String imageUrl;
  final String? heroTag;

  static void show(BuildContext context, String imageUrl, {String? heroTag}) {
    Navigator.of(context).push(
      PageRouteBuilder(
        opaque: false,
        barrierColor: context.kc.mediaBackdrop,
        pageBuilder: (context, _, __) => FullScreenImageViewer(
          imageUrl: imageUrl,
          heroTag: heroTag,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Scaffold(
      backgroundColor: kc.mediaBackdrop,
      extendBodyBehindAppBar: true,
      appBar: AppBar(
        backgroundColor: Colors.transparent,
        elevation: 0,
        leading: IconButton(
          tooltip: 'Kapat',
          icon: Icon(Icons.close_rounded, color: kc.onColor, size: 30),
          onPressed: () => Navigator.of(context).pop(),
        ),
      ),
      body: GestureDetector(
        onVerticalDragEnd: (details) {
          if (details.primaryVelocity != null && details.primaryVelocity! > 500) {
            Navigator.of(context).pop();
          }
        },
        child: Center(
          child: Hero(
            tag: heroTag ?? imageUrl,
            child: InteractiveViewer(
              minScale: 0.5,
              maxScale: 4.0,
              child: CachedNetworkImage(
                imageUrl: imageUrl,
                fit: BoxFit.contain,
                placeholder: (context, url) => Center(
                  child: CircularProgressIndicator(
                    color: kc.onColor.withValues(alpha: 0.7),
                  ),
                ),
                errorWidget: (context, url, error) => Icon(
                  Icons.error_outline,
                  color: kc.onColor,
                  size: 40,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
