import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/providers.dart';
import '../../../core/theme/app_typography.dart';
import '../../../core/theme/karar_tokens.dart';
import '../../../core/theme/motion.dart';
import '../../../core/theme/theme_provider.dart';
import '../../../shared/widgets/karar_avatar.dart';
import '../../../shared/widgets/karar_button.dart';
import '../../../shared/widgets/karar_circle_button.dart';
import '../../../shared/widgets/karar_logo.dart';
import '../../../shared/widgets/karar_pressable.dart';
import '../../../shared/widgets/karma_badge.dart';

/// Desktop side navigation (spec §5.2): canvas background with a 1 px
/// `outlineVariant` divider, 248 px expanded (≥ 1263 px) / 80 px collapsed.
///
/// Branch indices used by the shell:
/// Feed=0, Create=1, Notifications=2, Profile=3, Search=4, Discover=5.
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

  static const double expandedWidth = 248.0;
  static const double collapsedWidth = 80.0;

  /// Below this rendered width the labels are hidden (also while the width
  /// animates between the two states, so nothing overflows).
  static const double _labelThreshold = 168.0;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(currentUserProvider);
    final kc = context.kc;

    return AnimatedContainer(
      duration: KararMotion.d(context, KararMotion.nav),
      curve: KararMotion.standard,
      width: collapsed ? collapsedWidth : expandedWidth,
      // One structure in both themes: canvas + a 1 px divider, so the nav
      // has an edge in dark too (it used to blend into the #0D0C12 page).
      decoration: BoxDecoration(
        color: kc.canvas,
        border: Border(right: BorderSide(color: kc.outlineVariant)),
      ),
      child: LayoutBuilder(
        builder: (context, constraints) {
          final compact = constraints.maxWidth < _labelThreshold;
          return CustomScrollView(
            // Lets the CTA's shadow / focus ring paint up to the nav edge.
            clipBehavior: Clip.none,
            slivers: [
              SliverFillRemaining(
                hasScrollBody: false,
                child: Padding(
                  padding: const EdgeInsets.symmetric(
                    vertical: 18,
                    horizontal: 12,
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _LogoArea(
                        compact: compact,
                        onTap: () => onSelectIndex(0),
                      ),
                      const SizedBox(height: 8),
                      _SideNavItem(
                        icon: Icons.home_outlined,
                        selectedIcon: Icons.home_rounded,
                        label: 'Ana Sayfa',
                        isSelected: currentIndex == 0,
                        compact: compact,
                        onTap: () => onSelectIndex(0),
                      ),
                      const SizedBox(height: 4),
                      _SideNavItem(
                        icon: Icons.search_rounded,
                        selectedIcon: Icons.search_rounded,
                        label: 'Arama',
                        isSelected: currentIndex == 4,
                        compact: compact,
                        onTap: () => onSelectIndex(4),
                      ),
                      const SizedBox(height: 4),
                      _SideNavItem(
                        icon: Icons.explore_outlined,
                        selectedIcon: Icons.explore_rounded,
                        label: 'Keşfet',
                        isSelected: currentIndex == 5,
                        compact: compact,
                        onTap: () => onSelectIndex(5),
                      ),
                      const SizedBox(height: 4),
                      _SideNavItem(
                        icon: Icons.notifications_none_rounded,
                        selectedIcon: Icons.notifications_rounded,
                        label: 'Bildirimler',
                        isSelected: currentIndex == 2,
                        compact: compact,
                        onTap: () => onSelectIndex(2),
                        badge: unreadCount,
                      ),
                      const SizedBox(height: 14),
                      _CreateButton(
                        compact: compact,
                        isSelected: currentIndex == 1,
                        onTap: () => onSelectIndex(1),
                      ),
                      const Spacer(),
                      const SizedBox(height: 16),
                      if (user != null)
                        _SideNavItem(
                          icon: Icons.person_outline_rounded,
                          selectedIcon: Icons.person_rounded,
                          label: user.username,
                          isSelected: currentIndex == 3,
                          compact: compact,
                          onTap: () => onSelectIndex(3),
                          customIcon:
                              KararAvatar(username: user.username, radius: 15),
                          trailing: KarmaBadge(karma: user.karma, size: 14),
                          strongLabel: true,
                        )
                      else
                        _GuestAuthActions(compact: compact),
                      const SizedBox(height: 4),
                      _MoreTile(compact: compact),
                    ],
                  ),
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────────

class _LogoArea extends StatelessWidget {
  const _LogoArea({required this.compact, required this.onTap});

  final bool compact;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 72,
      child: Align(
        alignment: compact ? Alignment.center : Alignment.centerLeft,
        child: Padding(
          padding: EdgeInsets.only(left: compact ? 0 : 6),
          child: KararPressable(
            onTap: onTap,
            semanticLabel: 'Ana Sayfa',
            tooltip: compact ? 'Ana Sayfa' : null,
            pressedScale: 0.97,
            focusRadius: 20,
            child: compact
                ? const KararLogo(size: LogoSize.medium, showWordmark: false)
                : const KararLogo(size: LogoSize.medium),
          ),
        ),
      ),
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────────

/// Side-nav row: 52 tall, radius 18, H padding 14, icon 24 + 14 + label
/// 15/600 text2. Hover: muted 70 %. Active: primaryContainer, filled
/// `primary` icon, 15/800 `primaryText` label. Compact: icon only + tooltip.
class _SideNavItem extends StatefulWidget {
  const _SideNavItem({
    required this.icon,
    required this.selectedIcon,
    required this.label,
    required this.isSelected,
    required this.compact,
    required this.onTap,
    this.badge = 0,
    this.customIcon,
    this.trailing,
    this.strongLabel = false,
  });

  final IconData icon;
  final IconData selectedIcon;
  final String label;
  final bool isSelected;
  final bool compact;
  final VoidCallback onTap;
  final int badge;
  final Widget? customIcon;
  final Widget? trailing;

  /// Profile row: the username is 15/700 text1 (not the 600 text2 label).
  final bool strongLabel;

  @override
  State<_SideNavItem> createState() => _SideNavItemState();
}

class _SideNavItemState extends State<_SideNavItem> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final selected = widget.isSelected;

    final bgColor = selected
        ? kc.primaryContainer
        : _hovered
            ? kc.muted.withValues(alpha: 0.7)
            : kc.muted.withValues(alpha: 0);

    Widget iconChild = widget.customIcon ??
        Icon(
          selected ? widget.selectedIcon : widget.icon,
          size: 24,
          color: selected ? kc.primary : kc.text2,
        );

    final badgeCount = widget.badge.clamp(0, 99);
    if (widget.badge > 0 && widget.compact) {
      iconChild = Stack(
        clipBehavior: Clip.none,
        children: [
          iconChild,
          Positioned(
            top: -6,
            right: -10,
            child: _CountBadge(
              count: badgeCount,
              ringColor: selected ? kc.primaryContainer : kc.canvas,
            ),
          ),
        ],
      );
    }

    final labelStyle = (selected
            ? AppTypography.bodyStrong.copyWith(fontWeight: FontWeight.w800)
            : AppTypography.bodyStrong.copyWith(
                fontWeight:
                    widget.strongLabel ? FontWeight.w700 : FontWeight.w600,
              ))
        .copyWith(
      color: selected
          ? kc.primaryText
          : widget.strongLabel
              ? kc.text1
              : kc.text2,
    );

    final content = AnimatedContainer(
      duration: KararMotion.d(context, KararMotion.fast),
      curve: KararMotion.standard,
      width: double.infinity,
      constraints: const BoxConstraints(minHeight: 52),
      padding: EdgeInsets.symmetric(horizontal: widget.compact ? 0 : 14),
      decoration: BoxDecoration(
        color: bgColor,
        borderRadius: KararRadius.all(KararRadius.navItem),
      ),
      child: widget.compact
          ? Center(child: iconChild)
          : Row(
              children: [
                iconChild,
                const SizedBox(width: 14),
                Expanded(
                  child: Text(
                    widget.label,
                    style: labelStyle,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                if (widget.trailing != null) ...[
                  const SizedBox(width: 6),
                  widget.trailing!,
                ],
                if (widget.badge > 0) ...[
                  const SizedBox(width: 8),
                  _CountBadge(count: badgeCount),
                ],
              ],
            ),
    );

    return MouseRegion(
      onEnter: (_) => setState(() => _hovered = true),
      onExit: (_) => setState(() => _hovered = false),
      child: KararPressable(
        onTap: widget.onTap,
        semanticLabel: widget.label,
        semanticValue: widget.badge > 0 ? '$badgeCount' : null,
        selected: selected,
        tooltip: widget.compact ? widget.label : null,
        pressedScale: 0.98,
        focusRadius: KararRadius.navItem,
        child: content,
      ),
    );
  }
}

/// `haksizFill` count pill with white 11/800 digits; optional ring in the
/// row colour when it overlaps the icon (compact mode).
class _CountBadge extends StatelessWidget {
  const _CountBadge({required this.count, this.ringColor});

  final int count;
  final Color? ringColor;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return ExcludeSemantics(
      child: Container(
        constraints: const BoxConstraints(minWidth: 20, minHeight: 20),
        padding: const EdgeInsets.symmetric(horizontal: 5),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: kc.haksizFill,
          borderRadius: KararRadius.all(KararRadius.pill),
          border: ringColor == null
              ? null
              : Border.all(color: ringColor!, width: 2),
        ),
        child: Text(
          '$count',
          textScaler: TextScaler.noScaling,
          style: AppTypography.micro.copyWith(
            color: kc.onColor,
            height: 1,
            letterSpacing: 0,
            fontFeatures: AppTypography.tabular,
          ),
        ),
      ),
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────────

/// "Oluştur" CTA: `ink` pill (52 tall, radius 26) — near-black in light,
/// brand violet in dark (never an off-white block); compact: a 52 px ink
/// circle. The active create branch gets a soft brand glow (no outline
/// ring). Shadows stay inside the 12 px nav gutter: the page to the right
/// paints over anything that bleeds past the nav edge.
class _CreateButton extends StatelessWidget {
  const _CreateButton({
    required this.compact,
    required this.isSelected,
    required this.onTap,
  });

  final bool compact;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final decoration = BoxDecoration(
      color: kc.ink,
      borderRadius: KararRadius.all(26),
      boxShadow: [
        if (!kc.isDark)
          BoxShadow(
            color: kc.ink.withValues(alpha: 0.22),
            offset: const Offset(0, 6),
            blurRadius: 14,
            spreadRadius: -4,
          ),
        if (isSelected)
          BoxShadow(
            color: kc.primaryFill.withValues(alpha: kc.isDark ? 0.55 : 0.45),
            offset: const Offset(0, 4),
            blurRadius: 14,
            spreadRadius: -3,
          ),
      ],
    );

    final child = compact
        ? Container(
            width: 52,
            height: 52,
            decoration: decoration,
            alignment: Alignment.center,
            child: Icon(Icons.add_rounded, size: 24, color: kc.onInk),
          )
        : Container(
            width: double.infinity,
            constraints: const BoxConstraints(minHeight: 52),
            padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 8),
            decoration: decoration,
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Icon(Icons.add_rounded, size: 22, color: kc.onInk),
                const SizedBox(width: 8),
                Flexible(
                  child: Text(
                    'Oluştur',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: AppTypography.bodyStrong.copyWith(
                      fontWeight: FontWeight.w800,
                      color: kc.onInk,
                    ),
                  ),
                ),
              ],
            ),
          );

    return Align(
      alignment: Alignment.center,
      child: KararPressable(
        onTap: onTap,
        semanticLabel: 'Oluştur',
        selected: isSelected,
        tooltip: compact ? 'Oluştur' : null,
        pressedScale: 0.96,
        focusRadius: 26,
        child: child,
      ),
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────────

/// Logged-out footer (replaces the profile row): a "Giriş Yap" `ink` pill
/// (near-black in light, brand violet in dark) and a "Kayıt Ol" text
/// button, the same routes as the "Daha fazla" menu. Collapsed: a 48 px
/// ink circle with a person glyph and the "Giriş Yap" tooltip ("Kayıt Ol"
/// stays in the menu).
class _GuestAuthActions extends StatelessWidget {
  const _GuestAuthActions({required this.compact});

  final bool compact;

  @override
  Widget build(BuildContext context) {
    void login() => context.push('/auth/login');
    void register() => context.push('/auth/register');

    if (compact) {
      return Center(
        child: KararCircleButton(
          icon: Icons.person_outline_rounded,
          tooltip: 'Giriş Yap',
          onPressed: login,
          size: 48,
          iconSize: 24,
          style: KararCircleStyle.ink,
        ),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        KararButton(
          label: 'Giriş Yap',
          icon: Icons.login_rounded,
          variant: KararButtonVariant.ink,
          onPressed: login,
        ),
        const SizedBox(height: 4),
        KararButton(
          label: 'Kayıt Ol',
          variant: KararButtonVariant.text,
          onPressed: register,
        ),
      ],
    );
  }
}

// ─────────────────────────────────────────────────────────────────────────────

class _MoreTile extends ConsumerStatefulWidget {
  const _MoreTile({required this.compact});

  final bool compact;

  @override
  ConsumerState<_MoreTile> createState() => _MoreTileState();
}

class _MoreTileState extends ConsumerState<_MoreTile> {
  bool _hovered = false;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final themeMode = ref.watch(themeProvider);
    final user = ref.watch(currentUserProvider);

    final bgColor = _hovered
        ? kc.muted.withValues(alpha: 0.7)
        : kc.muted.withValues(alpha: 0);

    final tileContent = AnimatedContainer(
      duration: KararMotion.d(context, KararMotion.fast),
      curve: KararMotion.standard,
      width: double.infinity,
      constraints: const BoxConstraints(minHeight: 52),
      padding: EdgeInsets.symmetric(horizontal: widget.compact ? 0 : 14),
      decoration: BoxDecoration(
        color: bgColor,
        borderRadius: KararRadius.all(KararRadius.navItem),
      ),
      child: widget.compact
          ? Center(child: Icon(Icons.menu_rounded, size: 24, color: kc.text2))
          : Row(
              children: [
                Icon(Icons.menu_rounded, size: 24, color: kc.text2),
                const SizedBox(width: 14),
                Expanded(
                  child: Text(
                    'Daha fazla',
                    style: AppTypography.bodyStrong.copyWith(
                      fontWeight: FontWeight.w600,
                      color: kc.text2,
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
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
          const PopupMenuItem(
            value: 'settings',
            child: _PopupItem(icon: Icons.settings_rounded, label: 'Ayarlar'),
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
            const PopupMenuItem(
              value: 'logout',
              child: _PopupItem(icon: Icons.logout_rounded, label: 'Çıkış Yap'),
            )
          else ...[
            const PopupMenuItem(
              value: 'login',
              child: _PopupItem(icon: Icons.login_rounded, label: 'Giriş Yap'),
            ),
            const PopupMenuItem(
              value: 'register',
              child: _PopupItem(
                icon: Icons.person_add_alt_rounded,
                label: 'Kayıt Ol',
              ),
            ),
          ],
        ],
        child: Semantics(
          button: true,
          label: 'Daha fazla',
          excludeSemantics: true,
          child: tileContent,
        ),
      ),
    );

    if (widget.compact) {
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
        ThemeMode.light => Icons.light_mode_rounded,
        ThemeMode.dark => Icons.dark_mode_rounded,
        ThemeMode.system => Icons.brightness_auto_rounded,
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
    final kc = context.kc;
    return Row(
      children: [
        Icon(icon, size: 20, color: kc.text2),
        const SizedBox(width: 12),
        Text(label),
      ],
    );
  }
}
