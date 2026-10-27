import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/pwa_helper.dart';
import '../../shared/widgets/connectivity_banner.dart';
import '../../shared/widgets/content_policy_update_banner.dart';
import '../../shared/widgets/karar_surface.dart';
import '../../shared/widgets/kvkk_banner.dart';
import '../../shared/widgets/login_nudge.dart';
import '../feed/feed_provider.dart';
import '../notifications/notifications_provider.dart';
import 'widgets/karar_bottom_nav.dart';
import 'widgets/karar_side_nav.dart';

class HomeShell extends ConsumerStatefulWidget {
  const HomeShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  ConsumerState<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends ConsumerState<HomeShell>
    with WidgetsBindingObserver {
  bool _showPwaBanner = false;
  bool _iosGuide = false;
  bool _textInputFocused = false;

  static const _dismissKey = 'pwa_dismiss_ms';
  static const _dismissDuration = Duration(days: 7);

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _initPwaBanner();
    FocusManager.instance.addListener(_handleFocusChanged);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    FocusManager.instance.removeListener(_handleFocusChanged);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      ref.read(notificationsProvider.notifier).syncUnreadCount();
    }
  }

  void _handleFocusChanged() {
    final focused = _isTextInputFocused();
    if (focused == _textInputFocused || !mounted) return;
    setState(() => _textInputFocused = focused);
  }

  bool _isTextInputFocused() {
    FocusNode? node = FocusManager.instance.primaryFocus;
    while (node != null) {
      final context = node.context;
      if (context != null &&
          (context.widget is EditableText ||
              context.findAncestorWidgetOfExactType<EditableText>() != null)) {
        return true;
      }
      node = node.parent;
    }
    return false;
  }

  Future<void> _initPwaBanner() async {
    if (PwaHelper.isInstalled) return;

    final prefs = await SharedPreferences.getInstance();
    final dismissedAt = prefs.getInt(_dismissKey);
    if (dismissedAt != null) {
      final elapsed = DateTime.now().millisecondsSinceEpoch - dismissedAt;
      if (elapsed < _dismissDuration.inMilliseconds) return;
    }

    if (PwaHelper.isIosSafari) {
      if (mounted) {
        setState(() {
          _showPwaBanner = true;
          _iosGuide = true;
        });
      }
      return;
    }

    PwaHelper.setOnInstallPromptListener(() {
      if (mounted) setState(() => _showPwaBanner = true);
    });
  }

  Future<void> _dismissPwaBanner() async {
    setState(() => _showPwaBanner = false);
    final prefs = await SharedPreferences.getInstance();
    await prefs.setInt(_dismissKey, DateTime.now().millisecondsSinceEpoch);
  }

  void _moveFeedFocus(int delta) {
    if (widget.navigationShell.currentIndex != 0) return;
    final feedState = ref.read(feedProvider);
    final count = feedState.posts.length;
    if (count == 0) return;
    final current = ref.read(feedFocusIndexProvider);
    final next = current == null
        ? (delta > 0 ? 0 : count - 1)
        : (current + delta).clamp(0, count - 1);
    ref.read(feedFocusIndexProvider.notifier).state = next;
  }

  void _openFocusedFeedPost() {
    if (widget.navigationShell.currentIndex != 0) return;
    final index = ref.read(feedFocusIndexProvider);
    if (index == null) return;
    final posts = ref.read(feedProvider).posts;
    if (index < posts.length) {
      context.push('/posts/${posts[index].id}', extra: posts[index]);
    }
  }

  void _onDestinationSelected(int index) {
    // Auth required only for Create (1), Notifications (2), Profile (3).
    // Feed (0), Search (4), Discover (5) are public.
    const authRequired = {1, 2, 3};
    if (authRequired.contains(index)) {
      final isLoggedIn = ref.read(currentUserProvider) != null;
      if (!isLoggedIn) {
        final (title, message) = switch (index) {
          1 => (
              'Durumunu Anlat',
              'Toplulukla bir durum paylaşmak ve karar vermelerini sağlamak için giriş yapmalısın.'
            ),
          2 => (
              'Bildirimlerini Gör',
              'Sana gelen yanıtları ve önemli gelişmeleri takip etmek için giriş yapmalısın.'
            ),
          3 => (
              'Profilini Gör',
              'Paylaşımlarını ve istatistiklerini görmek için giriş yapmalısın.'
            ),
          _ => (
              'Giriş Gerekli',
              'Bu özelliği kullanmak için lütfen giriş yap.'
            ),
        };
        final returnTo = switch (index) {
          1 => '/create',
          2 => '/notifications',
          3 => '/profile',
          _ => '/',
        };

        LoginNudge.show(
          context,
          title: title,
          message: message,
          returnTo: returnTo,
        );
        return;
      }
    }

    widget.navigationShell.goBranch(
      index,
      initialLocation: index == widget.navigationShell.currentIndex,
    );
  }

  @override
  Widget build(BuildContext context) {
    final unreadCount = ref.watch(
      notificationsProvider.select((state) => state.unreadCount),
    );
    final isDesktop = context.isDesktop;
    final currentIndex = widget.navigationShell.currentIndex;

    Widget shell = Scaffold(
      // Mobile/tablet: the content runs under the floating pill nav; tabs
      // receive the nav height as MediaQuery bottom padding
      // (`context.navBottomInset`).
      extendBody: !isDesktop,
      body: Stack(
        children: [
          Column(
            children: [
              const ConnectivityBanner(),
              const ContentPolicyUpdateBanner(),
              Expanded(child: _buildLayout(context, unreadCount)),
            ],
          ),
          // Soft fade behind the floating nav (Keşfet pages are full-bleed).
          if (!isDesktop && currentIndex != 5)
            const Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              height: 150,
              child: _NavFade(),
            ),
          // PWA install + KVKK notices float as cards above the nav.
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: Builder(
              builder: (context) => Padding(
                padding: EdgeInsets.fromLTRB(
                  12,
                  0,
                  12,
                  MediaQuery.paddingOf(context).bottom + 12,
                ),
                child: Align(
                  alignment: Alignment.bottomCenter,
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 560),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        if (_showPwaBanner)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 8),
                            child: _buildPwaBanner(),
                          ),
                        MediaQuery.removePadding(
                          context: context,
                          removeBottom: true,
                          child: const _FloatingCard(child: KvkkBanner()),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
      bottomNavigationBar: isDesktop
          ? null
          : KararBottomNav(
              currentIndex: currentIndex,
              onSelect: _onDestinationSelected,
              unreadCount: unreadCount,
              hidden: _textInputFocused,
            ),
    );

    // Keyboard shortcuts for Web/Desktop — skip when any text input is focused
    if (_textInputFocused) return shell;

    return CallbackShortcuts(
      bindings: {
        // 1=Ana Sayfa, 2=Arama, 3=Keşfet, 4=Bildirimler, 5=Oluştur
        const SingleActivator(LogicalKeyboardKey.digit1): () {
          widget.navigationShell.goBranch(0);
        },
        const SingleActivator(LogicalKeyboardKey.digit2): () {
          widget.navigationShell.goBranch(4);
        },
        const SingleActivator(LogicalKeyboardKey.digit3): () {
          widget.navigationShell.goBranch(5);
        },
        const SingleActivator(LogicalKeyboardKey.digit4): () {
          widget.navigationShell.goBranch(2);
        },
        const SingleActivator(LogicalKeyboardKey.digit5): () {
          widget.navigationShell.goBranch(1);
        },
        const SingleActivator(LogicalKeyboardKey.slash): () {
          widget.navigationShell.goBranch(4);
        },
        const SingleActivator(LogicalKeyboardKey.keyN): () {
          widget.navigationShell.goBranch(1);
        },
        const SingleActivator(LogicalKeyboardKey.keyR): () {
          if (widget.navigationShell.currentIndex == 0) {
            ref.read(feedProvider.notifier).refresh();
            ref.read(feedFocusIndexProvider.notifier).state = null;
          }
        },
        const SingleActivator(LogicalKeyboardKey.keyJ): () {
          _moveFeedFocus(1);
        },
        const SingleActivator(LogicalKeyboardKey.keyK): () {
          _moveFeedFocus(-1);
        },
        const SingleActivator(LogicalKeyboardKey.enter): () {
          _openFocusedFeedPost();
        },
      },
      child: shell,
    );
  }

  Widget _buildPwaBanner() {
    final kc = context.kc;
    final titleStyle = AppTypography.labelS.copyWith(
      fontWeight: FontWeight.w800,
      color: kc.text1,
    );
    final bodyStyle = AppTypography.meta.copyWith(
      fontWeight: FontWeight.w500,
      letterSpacing: 0,
      height: 1.4,
      color: kc.text2,
    );

    final icon = ExcludeSemantics(
      child: Container(
        width: 40,
        height: 40,
        decoration: BoxDecoration(
          color: kc.primaryContainer,
          borderRadius: KararRadius.all(KararRadius.iconTile),
        ),
        alignment: Alignment.center,
        child: Icon(
          _iosGuide ? Icons.ios_share_rounded : Icons.install_mobile_rounded,
          size: 20,
          color: kc.primary,
        ),
      ),
    );

    if (_iosGuide) {
      return KararSurface(
        radius: KararRadius.tile,
        shadow: KararShadowKind.float,
        color: kc.isDark ? kc.elevated : kc.surface,
        padding: const EdgeInsets.fromLTRB(14, 12, 4, 12),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            icon,
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text('Karar\'ı cihazına ekle', style: titleStyle),
                  const SizedBox(height: 2),
                  Text(
                    'Safari\'de  Paylaş  ikonuna dokun, "Ana Ekrana Ekle" seçeneğini seç.',
                    style: bodyStyle,
                  ),
                ],
              ),
            ),
            IconButton(
              icon: const Icon(Icons.close_rounded, size: 20),
              color: kc.text2,
              onPressed: _dismissPwaBanner,
              tooltip: 'Kapat',
            ),
          ],
        ),
      );
    }

    return KararSurface(
      radius: KararRadius.tile,
      shadow: KararShadowKind.float,
      color: kc.isDark ? kc.elevated : kc.surface,
      padding: const EdgeInsets.fromLTRB(14, 12, 12, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            children: [
              icon,
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text('Karar\'ı cihazına ekle', style: titleStyle),
                    const SizedBox(height: 2),
                    Text(
                      'Daha hızlı açılır, bildirim alırsın.',
                      style: bodyStyle,
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Wrap(
            alignment: WrapAlignment.end,
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 4,
            children: [
              TextButton(
                onPressed: _dismissPwaBanner,
                child: const Text('Şimdilik değil'),
              ),
              FilledButton(
                onPressed: () {
                  PwaHelper.promptInstall();
                  setState(() => _showPwaBanner = false);
                },
                style: FilledButton.styleFrom(
                  minimumSize: const Size(64, 44),
                  padding: const EdgeInsets.symmetric(horizontal: 20),
                  textStyle: AppTypography.label,
                ),
                child: const Text('Ekle'),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildLayout(BuildContext context, int unreadCount) {
    if (context.isDesktop) {
      return Row(
        children: [
          KararSideNav(
            currentIndex: widget.navigationShell.currentIndex,
            onSelectIndex: _onDestinationSelected,
            unreadCount: unreadCount,
            collapsed: !context.isSideNavExpanded,
          ),
          Expanded(child: widget.navigationShell),
        ],
      );
    }

    return widget.navigationShell;
  }
}

/// Canvas fade behind the floating nav (canvas 0 % → 92 % at 70 %).
class _NavFade extends StatelessWidget {
  const _NavFade();

  @override
  Widget build(BuildContext context) {
    final canvas = context.kc.canvas;
    return IgnorePointer(
      child: ExcludeSemantics(
        child: DecoratedBox(
          decoration: BoxDecoration(
            gradient: LinearGradient(
              begin: Alignment.topCenter,
              end: Alignment.bottomCenter,
              colors: [
                canvas.withValues(alpha: 0),
                canvas.withValues(alpha: 0.92),
              ],
              stops: const [0, 0.7],
            ),
          ),
        ),
      ),
    );
  }
}

/// Rounded floating frame (radius 20, float shadow) for notices that paint
/// their own background (KVKK). Paints nothing visible while the notice is
/// hidden (zero size).
class _FloatingCard extends StatelessWidget {
  const _FloatingCard({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final radius = KararRadius.all(KararRadius.tile);
    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: radius,
        boxShadow: KararShadow.float(Theme.of(context).brightness),
      ),
      child: ClipRRect(borderRadius: radius, child: child),
    );
  }
}
