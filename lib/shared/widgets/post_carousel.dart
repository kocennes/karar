import 'package:flutter/material.dart';
import 'post_image.dart';

class PostCarousel extends StatefulWidget {
  const PostCarousel({
    super.key,
    required this.imageUrls,
    this.aspectRatio = 16 / 9,
    this.borderRadius = 12,
    this.enableFullScreen = true,
    this.postId,
  });

  final List<String> imageUrls;
  final double aspectRatio;
  final double borderRadius;
  final bool enableFullScreen;
  final String? postId;

  @override
  State<PostCarousel> createState() => _PostCarouselState();
}

class _PostCarouselState extends State<PostCarousel> {
  final _controller = PageController();
  int _currentPage = 0;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (widget.imageUrls.isEmpty) return const SizedBox.shrink();
    if (widget.imageUrls.length == 1) {
      return PostImage(
        imageUrl: widget.imageUrls.first,
        aspectRatio: widget.aspectRatio,
        borderRadius: widget.borderRadius,
        enableFullScreen: widget.enableFullScreen,
        heroTag: widget.postId != null ? 'post_image_${widget.postId}_0' : null,
      );
    }

    return AspectRatio(
      aspectRatio: widget.aspectRatio,
      child: Stack(
        children: [
          PageView.builder(
            controller: _controller,
            itemCount: widget.imageUrls.length,
            onPageChanged: (i) => setState(() => _currentPage = i),
            itemBuilder: (context, index) {
              return PostImage(
                imageUrl: widget.imageUrls[index],
                borderRadius: widget.borderRadius,
                enableFullScreen: widget.enableFullScreen,
                heroTag: widget.postId != null
                    ? 'post_image_${widget.postId}_$index'
                    : null,
              );
            },
          ),
          if (widget.imageUrls.length > 1) ...[
            Positioned(
              bottom: 12,
              left: 0,
              right: 0,
              child: Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: List.generate(
                  widget.imageUrls.length,
                  (index) => Container(
                    width: 6,
                    height: 6,
                    margin: const EdgeInsets.symmetric(horizontal: 3),
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: _currentPage == index
                          ? Colors.white
                          : Colors.white.withValues(alpha: 0.5),
                      boxShadow: const [
                        BoxShadow(
                          color: Colors.black26,
                          blurRadius: 4,
                          offset: Offset(0, 1),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
            Positioned(
              top: 12,
              right: 12,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                decoration: BoxDecoration(
                  color: Colors.black54,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Text(
                  '${_currentPage + 1}/${widget.imageUrls.length}',
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 10,
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
