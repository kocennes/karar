import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/providers.dart';
import '../../../core/theme/app_colors.dart';
import '../../../core/theme/theme_provider.dart';
import '../../../shared/widgets/karar_avatar.dart';
import '../../../shared/widgets/karar_logo.dart';

/// Branch indices used by the shell.
/// Feed=0, Create=1, Notifications=2, Profile=3, Search=4, Discover=5
class KararSideNav extends ConsumerWidget {
  const KararSideNav({
    super.key,
    required this.currentIndex,
    required this.onSelectIndex,
    required this.unreadCount,
    required this.collapsed,
  });

  final int currentIndex;
  final ValueChanged<int> onSelectIndex;
  final int unreadCount;
  final bool collapsed;

  static const double expandedWidth = 244.0;
  static const double collapsedWidth = 72.0;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(currentUserProvider);
    final colorScheme = Theme.of(context).colorScheme;

    return AnimatedContainer(
      duration: const Duration(milliseconds: 200),
      curve: Curves.easeInOut,
      width: collapsed ? collapsedWidth : expandedWidth,
      color: colorScheme.surface,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _LogoArea(
              collapsed: collapsed,
              onTap: () => onSelectIndex(0),
            ),
            const SizedBox(height: 4),
            _SideNavTile(
              icon: Icons.home_outlined,
              selectedIcon: Icons.home,
              label: 'Ana Sayfa',
              isSelected: currentIndex == 0,
              collapsed: collapsed,
              onTap: () => onSelectIndex(0),
            ),
            _SideNavTile(
              icon: Icons.search,
              selectedIcon: Icons.search,
              label: 'Arama',
              isSelected: currentIndex == 4,
              collapsed: collapsed,
              onTap: () => onSelectIndex(4),
            ),
            _SideNavTile(
              icon: Icons.explore_outlined,
              selectedIcon: Icons.explore,
              label: 'Keşfet',
              isSelected: currentIndex == 5,
              collapsed: collapsed,
              onTap: () => onSelectIndex(5),
            ),
            _SideNavTile(
              icon: Icons.notifications_none,
              selectedIcon: Icons.notifications,
              label: 'Bildirimler',
              isSelected: currentIndex == 2,
              collapsed: collapsed,
              onTap: () => onSelectIndex(2),
              badge: unreadCount,
            ),
            _SideNavTile(
              icon: Icons.add_circle_outline,
              selectedIcon: Icons.add_circle,
              label: 'Oluştur',
              isSelected: currentIndex == 1,
              collapsed: collapsed,
              onTap: () => onSelectIndex(1),
            ),
            const Spacer(),
            _SideNavTile(
              icon: Icons.person_outline,
              selectedIcon: Icons.person,
              label: user?.username ?? 'Profil',
              isSelected: currentIndex == 3,
              collapsed: collapsed,
              onTap: () => onSelectIndex(3),
              customIcon: user != null
                  ? KararAvatar(username: user.username, radius: 13)
                  : null,
            ),
            _MoreTile(collapsed: collapsed),
            const SizedBox(height: 8),
          ],
        ),
      ),
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────────

class _LogoArea extends StatelessWidget {
  const _LogoArea({required this.collapsed, required this.onTap});

  final bool collapsed;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: EdgeInsets.fromLTRB(
        collapsed ? 16 : 20,
        20,
        collapsed ? 16 : 20,
        8,
      ),
      child: MouseRegion(
        cursor: SystemMouseCursors.click,
        child: GestureDetector(
          onTap: onTap,
          child: collapsed
              ? _iconOnly(context)
              : const KararLogo(size: LogoSize.medium),
        ),
      ),
    );
  }

  Widget _iconOnly(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final boxColor = isDark
        ? AppColors.darkSurfaceContainerHighest
        : AppColors.surfaceContainerHighest;
    final boxBorder = isDark ? AppColors.darkBorder : AppColors.border;
    final iconColor =
        isDark ? AppColors.darkTextPrimary : AppColors.textPrimary;
    final hakliColor = isDark ? AppColors.darkHakli : AppColors.hakli;
    final haksizColor = isDark ? AppColors.darkHaksiz : AppColors.haksiz;

    const box = 38.0;
    const badge = 13.0;

    return SizedBox(
      width: box + badge * 0.6,
      height: box + badge * 0.6,
      child: Stack(
        children: [
          Positioned(
            bottom: 0,
            right: 0,
            child: Container(
              width: box,
              height: box,
              decoration: BoxDecoration(
                color: boxColor,
                borderRadius: BorderRadius.circular(box * 0.28),
                border: Border.all(color: boxBorder),
              ),
              child: Center(
                child: Icon(Icons.balance_rounded, size: 19, color: iconColor),
              ),
            ),
          ),
          Positioned(
            top: 0,
            left: 0,
            child: Container(
              width: badge,
              height: badge,
              decoration:
                  BoxDecoration(color: hakliColor, shape: BoxShape.circle),
              child: Center(
                  child: Icon(Icons.check, size: 7, color: Colors.white)),
            ),
          ),
          Positioned(
            bottom: 0,
            right: 0,
            child: Container(
              width: badge,
              height: badge,
              decoration:
                  BoxDecoration(color: haksizColor, shape: BoxShape.circle),
              child: Center(
                  child: Icon(Icons.close, size: 7, color: Colors.white)),
            ),
          ),
        ],
      ),
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────────

class _SideNavTile extends StatefulWidget {
  const _SideNavTile({
    required this.icon,
    required this.selectedIcon,
    required this.label,
    required this.isSelected,
    required this.collapsed,
    required this.onTap,
    this.badge = 0,
    this.customIcon,
  });

  final IconData icon;
  final IconData selectedIcon;
  final String label;
  final bool isSelected;
  final bool collapsed;
  final VoidCallback onTap;
  final int badge;
  final Widget? customIcon;

  @override
  State<_SideNavTile> createState() => _SideNavTileState();
}

class _SideNavTileState extends State<_SideNavTile> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;

    final bgColor = widget.isSelected
        ? colorScheme.onSurface.withValues(alpha: 0.12)
        : _hovered
            ? colorScheme.onSurface.withValues(alpha: 0.08)
            : Colors.transparent;

    Widget iconChild = widget.customIcon ??
        Icon(
          widget.isSelected ? widget.selectedIcon : widget.icon,
          size: 26,
          color: colorScheme.onSurface,
        );

    if (widget.badge > 0) {
      iconChild = Badge.count(
        count: widget.badge.clamp(0, 99),
        child: iconChild,
      );
    }

    final tile = AnimatedContainer(
      duration: const Duration(milliseconds: 100),
      margin: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: bgColor,
        borderRadius: BorderRadius.circular(12),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: widget.onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 11),
          child: Row(
            children: [
              iconChild,
              if (!widget.collapsed) ...[
                const SizedBox(width: 16),
                Expanded(
                  child: Text(
                    widget.label,
                    style: TextStyle(
                      fontSize: 15,
                      fontWeight: widget.isSelected
                          ? FontWeight.bold
                          : FontWeight.normal,
                      color: colorScheme.onSurface,
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );

    Widget child = MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      cursor: SystemMouseCursors.click,
      child: tile,
    );

    if (widget.collapsed) {
      return Tooltip(
        message: widget.label,
        preferBelow: false,
        waitDuration: const Duration(milliseconds: 400),
        child: child,
      );
    }

    return child;
  }
}

// ─────────────────────────────────────────────────────────────────────────────

class _MoreTile extends ConsumerStatefulWidget {
  const _MoreTile({required this.collapsed});

  final bool collapsed;

  @override
  ConsumerState<_MoreTile> createState() => _MoreTileState();
}

class _MoreTileState extends ConsumerState<_MoreTile> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final themeMode = ref.watch(themeProvider);
    final user = ref.watch(currentUserProvider);

    final bgColor = _hovered
        ? colorScheme.onSurface.withValues(alpha: 0.08)
        : Colors.transparent;

    final tileContent = AnimatedContainer(
      duration: const Duration(milliseconds: 100),
      margin: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: bgColor,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 11),
        child: Row(
          children: [
            const Icon(Icons.menu_rounded, size: 26),
            if (!widget.collapsed) ...[
              const SizedBox(width: 16),
              const Expanded(
                child: Text(
                  'Daha fazla',
                  style: TextStyle(fontSize: 15),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ],
        ),
      ),
    );

    Widget child = MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      cursor: SystemMouseCursors.click,
      child: PopupMenuButton<String>(
        tooltip: '',
        position: PopupMenuPosition.over,
        offset: const Offset(8, -12),
        onSelected: (value) => _handleAction(context, value),
        itemBuilder: (_) => [
          PopupMenuItem(
            value: 'settings',
            child: _PopupItem(icon: Icons.settings_outlined, label: 'Ayarlar'),
          ),
          PopupMenuItem(
            value: 'theme',
            child: _PopupItem(
              icon: _themeIcon(themeMode),
              label: _themeName(themeMode),
            ),
          ),
          const PopupMenuDivider(),
          if (user != null)
            PopupMenuItem(
              value: 'logout',
              child: _PopupItem(icon: Icons.logout, label: 'Çıkış Yap'),
            )
          else ...[
            PopupMenuItem(
              value: 'login',
              child: _PopupItem(icon: Icons.login, label: 'Giriş Yap'),
            ),
            PopupMenuItem(
              value: 'register',
              child: _PopupItem(
                  icon: Icons.person_add_outlined, label: 'Kayıt Ol'),
            ),
          ],
        ],
        child: tileContent,
      ),
    );

    if (widget.collapsed) {
      return Tooltip(
        message: 'Daha fazla',
        preferBelow: false,
        waitDuration: const Duration(milliseconds: 400),
        child: child,
      );
    }

    return child;
  }

  Future<void> _handleAction(BuildContext context, String action) async {
    switch (action) {
      case 'settings':
        context.push('/settings');
      case 'theme':
        _cycleTheme();
      case 'logout':
        final authService = ref.read(authServiceProvider);
        await authService.logout();
        ref.read(currentUserProvider.notifier).state = null;
        if (context.mounted) context.go('/');
      case 'login':
        context.push('/auth/login');
      case 'register':
        context.push('/auth/register');
    }
  }

  void _cycleTheme() {
    final current = ref.read(themeProvider);
    final next = switch (current) {
      ThemeMode.system => ThemeMode.light,
      ThemeMode.light => ThemeMode.dark,
      ThemeMode.dark => ThemeMode.system,
    };
    ref.read(themeProvider.notifier).setMode(next);
  }

  static IconData _themeIcon(ThemeMode mode) => switch (mode) {
        ThemeMode.light => Icons.light_mode_outlined,
        ThemeMode.dark => Icons.dark_mode_outlined,
        ThemeMode.system => Icons.brightness_auto_outlined,
      };

  static String _themeName(ThemeMode mode) => switch (mode) {
        ThemeMode.light => 'Açık mod',
        ThemeMode.dark => 'Koyu mod',
        ThemeMode.system => 'Sistem teması',
      };
}

// ─────────────────────────────────────────────────────────────────────────────

class _PopupItem extends StatelessWidget {
  const _PopupItem({required this.icon, required this.label});

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Icon(icon, size: 20),
        const SizedBox(width: 12),
        Text(label),
      ],
    );
  }
}
