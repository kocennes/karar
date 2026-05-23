import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';
import 'full_screen_image_viewer.dart';

class PostImage extends StatefulWidget {
  const PostImage({
    super.key,
    required this.imageUrl,
    this.width,
    this.height,
    this.aspectRatio,
    this.borderRadius = 12,
    this.memCacheWidth,
    this.fit = BoxFit.cover,
    this.enableFullScreen = false,
    this.heroTag,
    @visibleForTesting this.forceError = false,
  });

  final String imageUrl;
  final double? width;
  final double? height;
  final double? aspectRatio;
  final double borderRadius;
  final int? memCacheWidth;
  final BoxFit fit;
  final bool enableFullScreen;
  final String? heroTag;
  final bool forceError;

  @override
  State<PostImage> createState() => _PostImageState();
}

class _PostImageState extends State<PostImage> {
  var _retry = 0;
  var _isHidden = false;

  bool get _isCompact => (widget.width ?? double.infinity) <= 120;

  Future<void> _retryLoad() async {
    await CachedNetworkImage.evictFromCache(widget.imageUrl);
    if (!mounted) return;
    setState(() => _retry++);
  }

  void _hide() {
    setState(() => _isHidden = true);
  }

  @override
  Widget build(BuildContext context) {
    if (_isHidden) return const SizedBox.shrink();

    if (widget.forceError) {
      return _buildErrorFrame();
    }

    Widget image = ClipRRect(
      borderRadius: BorderRadius.circular(widget.borderRadius),
      child: Hero(
        tag: widget.heroTag ?? widget.imageUrl,
        child: CachedNetworkImage(
          key: ValueKey('${widget.imageUrl}:$_retry'),
          imageUrl: widget.imageUrl,
          width: widget.width,
          height: widget.height,
          memCacheWidth: widget.memCacheWidth,
          fit: widget.fit,
          placeholder: (context, url) => _ImageFrame(
            width: widget.width,
            height: widget.height,
            aspectRatio: widget.aspectRatio,
            child: const Center(
              child: SizedBox(
                width: 22,
                height: 22,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            ),
          ),
          errorWidget: (context, url, error) => _buildErrorFrame(),
        ),
      ),
    );

    if (widget.enableFullScreen) {
      image = GestureDetector(
        onTap: () => FullScreenImageViewer.show(
          context,
          widget.imageUrl,
          heroTag: widget.heroTag ?? widget.imageUrl,
        ),
        child: image,
      );
    }

    if (widget.aspectRatio == null) return image;
    return AspectRatio(aspectRatio: widget.aspectRatio!, child: image);
  }

  Widget _buildErrorFrame() {
    final frame = _ImageFrame(
      width: widget.width,
      height: widget.height,
      aspectRatio: widget.aspectRatio,
      child: _ImageErrorActions(
        isCompact: _isCompact,
        onRetry: _retryLoad,
        onHide: _hide,
      ),
    );

    return ClipRRect(
      borderRadius: BorderRadius.circular(widget.borderRadius),
      child: frame,
    );
  }
}

class _ImageFrame extends StatelessWidget {
  const _ImageFrame({
    required this.child,
    this.width,
    this.height,
    this.aspectRatio,
  });

  final Widget child;
  final double? width;
  final double? height;
  final double? aspectRatio;

  @override
  Widget build(BuildContext context) {
    final frame = Container(
      width: width,
      height: height,
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      child: child,
    );

    if (aspectRatio == null) return frame;
    return AspectRatio(aspectRatio: aspectRatio!, child: frame);
  }
}

class _ImageErrorActions extends StatelessWidget {
  const _ImageErrorActions({
    required this.isCompact,
    required this.onRetry,
    required this.onHide,
  });

  final bool isCompact;
  final VoidCallback onRetry;
  final VoidCallback onHide;

  @override
  Widget build(BuildContext context) {
    final color = Theme.of(context).colorScheme.onSurfaceVariant;

    if (isCompact) {
      return Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          _CompactIconAction(
            tooltip: 'Gorseli tekrar dene',
            onPressed: onRetry,
            icon: Icons.refresh,
            color: color,
          ),
          _CompactIconAction(
            tooltip: 'Gorseli gizle',
            onPressed: onHide,
            icon: Icons.visibility_off_outlined,
            color: color,
          ),
        ],
      );
    }

    return Column(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Icon(Icons.broken_image_outlined, color: color, size: 36),
        const SizedBox(height: 8),
        Wrap(
          alignment: WrapAlignment.center,
          spacing: 8,
          runSpacing: 4,
          children: [
            OutlinedButton.icon(
              onPressed: onRetry,
              icon: const Icon(Icons.refresh, size: 18),
              label: const Text('Tekrar dene'),
            ),
            TextButton.icon(
              onPressed: onHide,
              icon: const Icon(Icons.visibility_off_outlined, size: 18),
              label: const Text('Gizle'),
            ),
          ],
        ),
      ],
    );
  }
}

class _CompactIconAction extends StatelessWidget {
  const _CompactIconAction({
    required this.tooltip,
    required this.onPressed,
    required this.icon,
    required this.color,
  });

  final String tooltip;
  final VoidCallback onPressed;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Tooltip(
      message: tooltip,
      child: InkWell(
        onTap: onPressed,
        borderRadius: BorderRadius.circular(18),
        child: SizedBox(
          width: 36,
          height: 36,
          child: Icon(icon, color: color, size: 18),
        ),
      ),
    );
  }
}
