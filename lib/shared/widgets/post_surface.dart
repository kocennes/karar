import 'dart:math' as math;
import 'dart:ui' show ImageFilter;

import 'package:cached_network_image/cached_network_image.dart';
import 'package:flutter/material.dart';

import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../models/post.dart';

/// Background of every post-shaped surface (spec §4.3): feed card, detail
/// hero, Keşfet page, create compose card, tiles.
///
/// * Text posts: the category gradient + decor (white 18 % radial at the
///   top-left, white 10 % radial behind the rail at the right-middle, a
///   150 px “ watermark at white 10 %).
/// * Photo posts: the first image, `BoxFit.cover`, with the §2.4 scrims.
///   While it loads — and if it fails — the category gradient shows (never an
///   error frame inside a card).
/// * Light theme: coloured glow (`KararShadow.card`) / photo shadow.
///   Dark theme: no shadow, 1 px inner hairline white 8 %.
/// * [seen]: darker palette (photos +20 % black), no glow.
///
/// [child] is laid out on top and defines the size (the background fills
/// it). All decorative layers are excluded from semantics. Text on this
/// surface must be pure white.
class PostSurface extends StatelessWidget {
  const PostSurface({
    super.key,
    required Post this.post,
    required this.child,
    this.radius = KararRadius.card,
    this.seen = false,
    this.showQuote = true,
    this.liveBlurHost = false,
    this.obscurePhoto = false,
    this.showPhoto = true,
    this.heroTag,
  }) : style = null;

  /// A post-less tinted surface (compose card, category banner,
  /// onboarding): the [style] gradient with the text-post decor.
  const PostSurface.tinted({
    super.key,
    required CategoryStyle this.style,
    required this.child,
    this.radius = KararRadius.card,
    this.seen = false,
    this.showQuote = true,
  })  : post = null,
        liveBlurHost = false,
        obscurePhoto = false,
        showPhoto = false,
        heroTag = null;

  final Post? post;
  final CategoryStyle? style;
  final Widget child;
  final double radius;
  final bool seen;
  final bool showQuote;

  /// The surface hosts live backdrop blurs (glass rail / chips on a photo):
  /// the background gets its own repaint boundary so blurs re-sample
  /// cheaply. Wrap lists/page views of such surfaces in a `BackdropGroup`.
  final bool liveBlurHost;

  /// Blur the photo (σ 24) — sensitive content veil.
  final bool obscurePhoto;

  /// Set false to force the gradient even for photo posts (detail hero).
  final bool showPhoto;

  /// Optional hero tag for the photo.
  final String? heroTag;

  /// First image URL of [post], if any.
  static String? imageUrlOf(Post post) {
    if (!post.hasImage) return null;
    if (post.imageUrls.isNotEmpty && post.imageUrls.first.isNotEmpty) {
      return post.imageUrls.first;
    }
    final url = post.imageUrl;
    return (url == null || url.isEmpty) ? null : url;
  }

  /// Whether [post] renders as a photo card.
  static bool isPhotoPost(Post post) => imageUrlOf(post) != null;

  @override
  Widget build(BuildContext context) {
    final brightness = Theme.of(context).brightness;
    final isDark = brightness == Brightness.dark;
    final palette = style ??
        (post != null ? CategoryStyle.of(post!.category) : CategoryStyle.unknown);
    final url = (showPhoto && post != null) ? imageUrlOf(post!) : null;
    final isPhoto = url != null;
    final br = BorderRadius.circular(radius);

    Widget background = Stack(
      fit: StackFit.expand,
      children: [
        DecoratedBox(
          decoration: BoxDecoration(gradient: palette.gradientFor(seen: seen)),
        ),
        if (!isPhoto) ...[
          const DecoratedBox(
            decoration: BoxDecoration(
              gradient: RadialGradient(
                center: Alignment.topLeft,
                radius: 0.9,
                colors: [Color(0x2EFFFFFF), Color(0x00FFFFFF)],
                stops: [0, 0.6],
              ),
            ),
          ),
          const DecoratedBox(
            decoration: BoxDecoration(
              gradient: RadialGradient(
                center: Alignment.centerRight,
                radius: 0.6,
                colors: [Color(0x1AFFFFFF), Color(0x00FFFFFF)],
                stops: [0, 0.7],
              ),
            ),
          ),
          if (showQuote) const _QuoteWatermark(),
        ],
        if (isPhoto) ...[
          _Photo(url: url, obscure: obscurePhoto, heroTag: heroTag),
          if (seen) const ColoredBox(color: Color(0x33000000)),
          const DecoratedBox(
            decoration: BoxDecoration(gradient: KararGradients.photoScrim),
          ),
        ],
      ],
    );
    if (liveBlurHost) background = RepaintBoundary(child: background);

    Widget card = Stack(
      children: [
        Positioned.fill(child: ExcludeSemantics(child: background)),
        child,
        if (isDark)
          Positioned.fill(
            child: IgnorePointer(
              child: ExcludeSemantics(
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    borderRadius: br,
                    border: Border.all(color: KararShadow.cardHairlineDark),
                  ),
                ),
              ),
            ),
          ),
      ],
    );

    card = radius > 0
        ? ClipRRect(borderRadius: br, clipBehavior: Clip.antiAlias, child: card)
        : ClipRect(child: card);

    final shadows = seen
        ? const <BoxShadow>[]
        : isPhoto
            ? KararShadow.photoCard(brightness)
            : KararShadow.card(palette.glow, brightness);
    if (shadows.isEmpty) return card;
    return DecoratedBox(
      decoration: BoxDecoration(borderRadius: br, boxShadow: shadows),
      child: card,
    );
  }
}

class _QuoteWatermark extends StatelessWidget {
  const _QuoteWatermark();

  @override
  Widget build(BuildContext context) {
    return const Stack(
      children: [
        Positioned(
          left: 14,
          top: 34,
          child: Text(
            '“',
            textScaler: TextScaler.noScaling,
            style: TextStyle(
              fontFamily: 'PlusJakartaSans',
              fontSize: 150,
              height: 1,
              fontWeight: FontWeight.w800,
              color: Color(0x1AFFFFFF),
            ),
          ),
        ),
      ],
    );
  }
}

class _Photo extends StatelessWidget {
  const _Photo({required this.url, required this.obscure, this.heroTag});

  final String url;
  final bool obscure;
  final String? heroTag;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final width = constraints.maxWidth.isFinite
            ? constraints.maxWidth
            : MediaQuery.sizeOf(context).width;
        final cacheWidth = math.min(2048, math.max(1, (width * 2).round()));
        Widget image = CachedNetworkImage(
          imageUrl: url,
          fit: BoxFit.cover,
          width: double.infinity,
          height: double.infinity,
          memCacheWidth: cacheWidth,
          fadeInDuration: const Duration(milliseconds: 220),
          placeholder: (context, url) => const SizedBox.shrink(),
          errorWidget: (context, url, error) => const SizedBox.shrink(),
        );
        if (heroTag != null) image = Hero(tag: heroTag!, child: image);
        if (obscure) {
          image = ImageFiltered(
            imageFilter: ImageFilter.blur(sigmaX: 24, sigmaY: 24),
            child: image,
          );
        }
        return image;
      },
    );
  }
}
