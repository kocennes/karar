import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/utils/pwa_helper.dart';
import '../../shared/widgets/content_policy_update_banner.dart';
import '../../shared/widgets/kvkk_banner.dart';
import '../../shared/widgets/login_nudge.dart';
import '../feed/feed_provider.dart';
import '../notifications/notifications_provider.dart';

class HomeShell extends ConsumerStatefulWidget {
  const HomeShell({super.key, required this.navigationShell});

  final StatefulNavigationShell navigationShell;

  @override
  ConsumerState<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends ConsumerState<HomeShell> {
  bool _showPwaBanner = false;
  bool _iosGuide = false;
  bool _textInputFocused = false;

  static const _dismissKey = 'pwa_dismiss_ms';
  static const _dismissDuration = Duration(days: 7);

  @override
  void initState() {
    super.initState();
    _initPwaBanner();
    FocusManager.instance.addListener(_handleFocusChanged);
  }

  @override
  void dispose() {
    FocusManager.instance.removeListener(_handleFocusChanged);
    super.dispose();
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
    // Restricted branches: Create (1), Profile (2), Notifications (3)
    if (index > 0) {
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

    Widget shell = Scaffold(
      body: Column(
        children: [
          const ContentPolicyUpdateBanner(),
          if (_showPwaBanner) _buildPwaBanner(),
          Expanded(child: _buildLayout(context, unreadCount)),
          const KvkkBanner(),
        ],
      ),
      bottomNavigationBar: context.isDesktop
          ? null
          : NavigationBar(
              selectedIndex: widget.navigationShell.currentIndex,
              onDestinationSelected: _onDestinationSelected,
              destinations: [
                const NavigationDestination(
                    icon: Icon(Icons.home_outlined),
                    selectedIcon: Icon(Icons.home),
                    label: 'Anasayfa'),
                const NavigationDestination(
                    icon: Icon(Icons.edit_outlined),
                    selectedIcon: Icon(Icons.edit),
                    label: 'Paylaş'),
                NavigationDestination(
                  icon: _NotificationIcon(
                    icon: Icons.notifications_outlined,
                    unreadCount: unreadCount,
                  ),
                  selectedIcon: _NotificationIcon(
                    icon: Icons.notifications,
                    unreadCount: unreadCount,
                  ),
                  label: 'Bildirimler',
                ),
                const NavigationDestination(
                    icon: Icon(Icons.person_outline),
                    selectedIcon: Icon(Icons.person),
                    label: 'Profilim'),
              ],
            ),
    );

    // Keyboard shortcuts for Web/Desktop — skip when any text input is focused
    if (_textInputFocused) return shell;

    return CallbackShortcuts(
      bindings: {
        const SingleActivator(LogicalKeyboardKey.digit1): () {
          widget.navigationShell.goBranch(0);
        },
        const SingleActivator(LogicalKeyboardKey.digit2): () {
          widget.navigationShell.goBranch(1);
        },
        const SingleActivator(LogicalKeyboardKey.digit3): () {
          widget.navigationShell.goBranch(2);
        },
        const SingleActivator(LogicalKeyboardKey.digit4): () {
          widget.navigationShell.goBranch(3);
        },
        const SingleActivator(LogicalKeyboardKey.slash): () {
          context.push('/search');
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
    final scheme = Theme.of(context).colorScheme;
    if (_iosGuide) {
      return Container(
        color: scheme.primaryContainer,
        padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.ios_share_outlined, size: 20),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    'Karar\'ı cihazına ekle',
                    style: TextStyle(
                      fontWeight: FontWeight.bold,
                      fontSize: 13,
                      color: scheme.onPrimaryContainer,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    'Safari\'de  Paylaş  ikonuna dokun, "Ana Ekrana Ekle" seçeneğini seç.',
                    style: TextStyle(
                      fontSize: 12,
                      color: scheme.onPrimaryContainer,
                    ),
                  ),
                ],
              ),
            ),
            IconButton(
              icon: const Icon(Icons.close, size: 18),
              visualDensity: VisualDensity.compact,
              onPressed: _dismissPwaBanner,
              tooltip: 'Kapat',
            ),
          ],
        ),
      );
    }

    return Container(
      color: scheme.primaryContainer,
      padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
      child: Row(
        children: [
          const Icon(Icons.install_mobile, size: 20),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  'Karar\'ı cihazına ekle',
                  style: TextStyle(
                    fontWeight: FontWeight.bold,
                    fontSize: 13,
                    color: scheme.onPrimaryContainer,
                  ),
                ),
                Text(
                  'Daha hızlı açılır, bildirim alırsın.',
                  style: TextStyle(
                    fontSize: 12,
                    color: scheme.onPrimaryContainer,
                  ),
                ),
              ],
            ),
          ),
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
              padding: const EdgeInsets.symmetric(horizontal: 16),
              visualDensity: VisualDensity.compact,
            ),
            child: const Text('Ekle'),
          ),
        ],
      ),
    );
  }

  Widget _buildLayout(BuildContext context, int unreadCount) {
    if (context.isDesktop) {
      return Row(
        children: [
          NavigationRail(
            extended: true,
            minExtendedWidth: 220,
            selectedIndex: widget.navigationShell.currentIndex,
            onDestinationSelected: _onDestinationSelected,
            destinations: [
              const NavigationRailDestination(
                  icon: Icon(Icons.home_outlined),
                  selectedIcon: Icon(Icons.home),
                  label: Text('Ana Sayfa')),
              const NavigationRailDestination(
                  icon: Icon(Icons.edit_outlined),
                  selectedIcon: Icon(Icons.edit),
                  label: Text('Yaz')),
              NavigationRailDestination(
                icon: _NotificationIcon(
                  icon: Icons.notifications_outlined,
                  unreadCount: unreadCount,
                ),
                selectedIcon: _NotificationIcon(
                  icon: Icons.notifications,
                  unreadCount: unreadCount,
                ),
                label: const Text('Bildirimler'),
              ),
              const NavigationRailDestination(
                  icon: Icon(Icons.person_outline),
                  selectedIcon: Icon(Icons.person),
                  label: Text('Profil')),
            ],
          ),
          const VerticalDivider(width: 1),
          Expanded(child: widget.navigationShell),
        ],
      );
    }

    return widget.navigationShell;
  }
}

class _NotificationIcon extends StatelessWidget {
  const _NotificationIcon({
    required this.icon,
    required this.unreadCount,
  });

  final IconData icon;
  final int unreadCount;

  @override
  Widget build(BuildContext context) {
    return Badge.count(
      count: unreadCount.clamp(0, 99),
      isLabelVisible: unreadCount > 0,
      child: Icon(icon),
    );
  }
}
