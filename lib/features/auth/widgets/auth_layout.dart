import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../core/layout/breakpoints.dart';
import '../../../core/theme/app_typography.dart';
import '../../../core/theme/category_style.dart';
import '../../../core/theme/karar_tokens.dart';
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
  final Widget? leading;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final wide = context.screenWidth >= Breakpoints.mobile;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Center(
          child: leading ??
              const Hero(
                tag: 'auth_logo',
                child: KararLogo(size: LogoSize.large),
              ),
        ),
        const SizedBox(height: 28),
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

/// Desktop brand panel: violet gradient with soft blobs, the white logo,
/// the community sentence and three tilted decorative verdict cards.
class _BrandPanel extends StatelessWidget {
  const _BrandPanel();

  static final _samples = [
    _sample('a', 1, 73, 27),
    _sample('b', 2, 38, 62),
    _sample('c', 3, 91, 9),
  ];

  static Post _sample(String id, int categoryId, int hakli, int haksiz) => Post(
        id: 'auth-decor-$id',
        category: Category(id: categoryId, name: '', icon: ''),
        title: '',
        content: '',
        createdAgo: '',
        voteCountHakli: hakli,
        voteCountHaksiz: haksiz,
        commentCount: 0,
        comments: const [],
        createdAt: DateTime(2026),
      );

  @override
  Widget build(BuildContext context) {
    return ClipRect(
      child: DecoratedBox(
        decoration: const BoxDecoration(gradient: KararGradients.brand),
        child: Stack(
          children: [
            const Positioned(top: -120, right: -80, child: _Blob(size: 360)),
            const Positioned(bottom: -160, left: -100, child: _Blob(size: 420)),
            Padding(
              padding: const EdgeInsets.fromLTRB(56, 48, 56, 48),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  const KararLogo(
                    size: LogoSize.large,
                    wordmarkColor: Colors.white,
                  ),
                  const SizedBox(height: 28),
                  ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 440),
                    child: Text(
                      'Hüküm vermek için sabırsızlanan bir topluluk var.',
                      style: AppTypography.titleXL.copyWith(
                        fontSize: 28,
                        height: 36 / 28,
                        color: Colors.white,
                      ),
                    ),
                  ),
                  const SizedBox(height: 44),
                  ExcludeSemantics(
                    child: SizedBox(
                      height: 300,
                      width: 440,
                      child: Stack(
                        clipBehavior: Clip.none,
                        children: [
                          Positioned(
                            left: 0,
                            top: 10,
                            child: _MiniVerdictCard(
                              post: _samples[0],
                              style: CategoryStyle.magenta,
                              angle: -0.07,
                            ),
                          ),
                          Positioned(
                            left: 150,
                            top: 92,
                            child: _MiniVerdictCard(
                              post: _samples[1],
                              style: CategoryStyle.ocean,
                              angle: 0.05,
                            ),
                          ),
                          Positioned(
                            left: 40,
                            top: 178,
                            child: _MiniVerdictCard(
                              post: _samples[2],
                              style: CategoryStyle.amber,
                              angle: -0.03,
                            ),
                          ),
                        ],
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

class _Blob extends StatelessWidget {
  const _Blob({required this.size});

  final double size;

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: Container(
        width: size,
        height: size,
        decoration: const BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(
            colors: [Color(0x2EFFFFFF), Color(0x00FFFFFF)],
          ),
        ),
      ),
    );
  }
}

/// A small tilted text-post card: two white placeholder title bars and a
/// [VerdictChip] — decoration only, no copy.
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
    Widget bar(double width, double alpha) => Container(
          width: width,
          height: 12,
          decoration: BoxDecoration(
            color: Colors.white.withValues(alpha: alpha),
            borderRadius: KararRadius.all(6),
          ),
        );

    return Transform.rotate(
      angle: angle,
      child: SizedBox(
        width: 260,
        child: PostSurface.tinted(
          style: style,
          radius: KararRadius.surface,
          showQuote: false,
          child: Padding(
            padding: const EdgeInsets.fromLTRB(18, 18, 18, 16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                bar(180, 0.9),
                const SizedBox(height: 8),
                bar(120, 0.6),
                const SizedBox(height: 16),
                VerdictChip(post: post),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
