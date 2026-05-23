import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
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
        barrierColor: Colors.black,
        pageBuilder: (context, _, __) => FullScreenImageViewer(
          imageUrl: imageUrl,
          heroTag: heroTag,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: Colors.black,
      extendBodyBehindAppBar: true,
      appBar: AppBar(
        backgroundColor: Colors.transparent,
        elevation: 0,
        leading: IconButton(
          icon: const Icon(Icons.close, color: Colors.white, size: 30),
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
                placeholder: (context, url) => const Center(
                  child: CircularProgressIndicator(color: Colors.white70),
                ),
                errorWidget: (context, url, error) => const Icon(
                  Icons.error_outline,
                  color: Colors.white,
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
