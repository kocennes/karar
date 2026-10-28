import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/layout/breakpoints.dart';
import '../../../core/theme/app_typography.dart';
import '../../../core/theme/category_style.dart';
import '../../../core/theme/karar_tokens.dart';
import '../../../shared/data/sample_posts.dart';
import '../../../shared/models/post.dart';
import '../../../shared/widgets/karar_logo.dart';
import '../../../shared/widgets/post_surface.dart';
import '../../../shared/widgets/verdict_chip.dart';

/// Shared chrome of the auth screens (spec §6.8).
///
/// * Mobile/tablet: transparent app bar with only a back button (or a close
///   button that returns to the feed when there is nothing to pop), then the
///   form centred at max 420 px with 24/16/24/32 padding.
/// * Desktop (≥ 1024): split screen — a violet brand panel on the left and
///   the form (max 420) centred on the right.
class AuthScaffold extends StatelessWidget {
  const AuthScaffold({super.key, required this.child});

  final Widget child;

  Widget _leading(BuildContext context) {
    if (Navigator.of(context).canPop()) return const BackButton();
    return IconButton(
      icon: const Icon(Icons.close_rounded),
      tooltip: 'Kapat',
      onPressed: () => context.go('/'),
    );
  }

  @override
  Widget build(BuildContext context) {
    final form = SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(24, 16, 24, 32),
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 420),
          child: child,
        ),
      ),
    );

    if (!context.isDesktop) {
      return Scaffold(
        appBar: AppBar(
          automaticallyImplyLeading: false,
          leading: _leading(context),
          backgroundColor: Colors.transparent,
        ),
        body: SafeArea(top: false, child: form),
      );
    }

    return Scaffold(
      body: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Expanded(child: _BrandPanel()),
          Expanded(
            child: SafeArea(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(12, 12, 12, 0),
                    child: _leading(context),
                  ),
                  Expanded(child: Center(child: form)),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Centred header: logo (or [leading]), `titleXL` headline (28 on ≥ 600),
/// optional subtitle (`body` text2).
class AuthHeader extends StatelessWidget {
  const AuthHeader({
    super.key,
    required this.title,
    this.subtitle,
    this.leading,
  });

  final String title;

  /// String or a rich widget (e.g. an e-mail address in bold).
  final Widget? subtitle;

  /// Defaults to `KararLogo(large)`; pass an [AuthDisc] for status pages.
  /// On desktop the default logo is left out: the brand panel next to the
  /// form already shows it.
  final Widget? leading;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final wide = context.screenWidth >= Breakpoints.mobile;
    final showLeading = leading != null || !context.isDesktop;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (showLeading) ...[
          Center(
            child: leading ??
                const Hero(
                  tag: 'auth_logo',
                  child: KararLogo(size: LogoSize.large),
                ),
          ),
          const SizedBox(height: 28),
        ],
        Semantics(
          header: true,
          child: Text(
            title,
            textAlign: TextAlign.center,
            style: AppTypography.titleXL.copyWith(
              color: kc.text1,
              fontSize: wide ? 28 : 26,
              height: wide ? 34 / 28 : 31 / 26,
            ),
          ),
        ),
        if (subtitle != null) ...[
          const SizedBox(height: 8),
          DefaultTextStyle.merge(
            textAlign: TextAlign.center,
            style: AppTypography.body.copyWith(color: kc.text2),
            child: subtitle!,
          ),
        ],
        const SizedBox(height: 32),
      ],
    );
  }
}

/// 88 px status disc (verify e-mail, password reset, done).
class AuthDisc extends StatelessWidget {
  const AuthDisc({
    super.key,
    required this.icon,
    this.background,
    this.foreground,
  });

  final IconData icon;
  final Color? background;
  final Color? foreground;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return ExcludeSemantics(
      child: Container(
        width: 88,
        height: 88,
        decoration: BoxDecoration(
          color: background ?? kc.primaryContainer,
          shape: BoxShape.circle,
        ),
        child: Icon(icon, size: 40, color: foreground ?? kc.primary),
      ),
    );
  }
}

/// Error message box: errorContainer, radius 16.
class AuthErrorBox extends StatelessWidget {
  const AuthErrorBox({super.key, required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Semantics(
      liveRegion: true,
      child: Container(
        padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
        decoration: BoxDecoration(
          color: scheme.errorContainer,
          borderRadius: KararRadius.all(KararRadius.input),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(Icons.error_outline_rounded,
                size: 20, color: scheme.onErrorContainer),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                message,
                style: AppTypography.bodyS.copyWith(
                  color: scheme.onErrorContainer,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// "veya" separator between the primary and the social CTA.
class AuthOrDivider extends StatelessWidget {
  const AuthOrDivider({super.key});

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Row(
      children: [
        Expanded(child: Divider(color: kc.outlineVariant)),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Text(
            'veya',
            style: AppTypography.meta.copyWith(color: kc.text3),
          ),
        ),
        Expanded(child: Divider(color: kc.outlineVariant)),
      ],
    );
  }
}

/// Desktop brand panel: violet gradient with soft blobs, the logo in white,
/// the community sentence and three tilted decorative verdict cards (the
/// in-app sample posts), each card's verdict chip fully visible.
class _BrandPanel extends StatelessWidget {
  const _BrandPanel();

  /// Decorative card palettes (contrast against the violet panel; the
  /// cards' own categories are not shown).
  static const _styles = [
    CategoryStyle.magenta,
    CategoryStyle.ocean,
    CategoryStyle.amber,
  ];

  /// Card origins and tilts inside the 470 × 392 collage: staggered so no
  /// card covers another card's verdict chip.
  static const _layout = [
    (left: 0.0, top: 0.0, angle: -0.06),
    (left: 190.0, top: 132.0, angle: 0.05),
    (left: 28.0, top: 264.0, angle: -0.03),
  ];

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final samples = samplePosts.take(_layout.length).toList();

    return ClipRect(
      child: DecoratedBox(
        decoration: const BoxDecoration(gradient: KararGradients.brand),
        child: Stack(
          children: [
            const Positioned(top: -120, right: -80, child: _Blob(size: 360)),
            const Positioned(bottom: -160, left: -100, child: _Blob(size: 420)),
            Padding(
              padding: const EdgeInsets.fromLTRB(56, 48, 40, 48),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  const _PanelLogo(),
                  const SizedBox(height: 28),
                  ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 440),
                    child: Text(
                      'Hüküm vermek için sabırsızlanan bir topluluk var.',
                      style: AppTypography.titleXL.copyWith(
                        fontSize: 28,
                        height: 36 / 28,
                        color: kc.onColor,
                      ),
                    ),
                  ),
                  const SizedBox(height: 40),
                  // Scales down on narrow desktop panels instead of being
                  // clipped at the panel edge.
                  Flexible(
                    child: FittedBox(
                      fit: BoxFit.scaleDown,
                      alignment: AlignmentDirectional.topStart,
                      child: ExcludeSemantics(
                        child: SizedBox(
                          width: 470,
                          height: 392,
                          child: Stack(
                            clipBehavior: Clip.none,
                            children: [
                              for (var i = 0; i < samples.length; i++)
                                Positioned(
                                  left: _layout[i].left,
                                  top: _layout[i].top,
                                  child: _MiniVerdictCard(
                                    post: samples[i],
                                    style: _styles[i % _styles.length],
                                    angle: _layout[i].angle,
                                  ),
                                ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The logo for the violet panel: a white disc with a brand-violet balance
/// glyph (4.91:1) and a white wordmark. The regular purple mark melted into
/// the panel gradient (1.25:1).
class _PanelLogo extends StatelessWidget {
  const _PanelLogo();

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Semantics(
      label: 'karar',
      container: true,
      child: ExcludeSemantics(
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 48,
              height: 48,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: kc.onColor,
                shape: BoxShape.circle,
              ),
              child: Icon(
                Icons.balance_rounded,
                size: 24,
                color: kc.primaryFill,
              ),
            ),
            const SizedBox(width: 10),
            Text(
              'karar',
              style: TextStyle(
                fontFamily: AppTypography.fontFamily,
                fontSize: 26,
                fontWeight: FontWeight.w800,
                letterSpacing: -1.0,
                height: 1,
                color: kc.onColor,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Blob extends StatelessWidget {
  const _Blob({required this.size});

  final double size;

  @override
  Widget build(BuildContext context) {
    final onColor = context.kc.onColor;
    return IgnorePointer(
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(
            colors: [
              onColor.withValues(alpha: 0.18),
              onColor.withValues(alpha: 0),
            ],
          ),
        ),
      ),
    );
  }
}

/// A small tilted text-post card: the post title (15/800 white, 2 lines)
/// and its [VerdictChip] — decoration only (excluded from semantics).
class _MiniVerdictCard extends StatelessWidget {
  const _MiniVerdictCard({
    required this.post,
    required this.style,
    required this.angle,
  });

  final Post post;
  final CategoryStyle style;

  /// Tilt in radians.
  final double angle;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Transform.rotate(
      angle: angle,
      child: SizedBox(
        width: 270,
        child: PostSurface.tinted(
          style: style,
          radius: KararRadius.surface,
          showQuote: false,
          showRailSheen: false,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(18, 18, 18, 16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  post.title,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  textScaler: TextScaler.noScaling,
                  style: AppTypography.bodyStrong.copyWith(
                    fontSize: 15,
                    height: 1.3,
                    fontWeight: FontWeight.w800,
                    color: kc.onColor,
                  ),
                ),
                const SizedBox(height: 14),
                MediaQuery.withNoTextScaling(
                  child: VerdictChip(post: post),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
