import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_colors.dart';
import 'admin_service.dart';

class AdminShell extends StatelessWidget {
  const AdminShell({super.key, required this.child, required this.adminService});
  final Widget child;
  final AdminService adminService;

  static const _items = [
    _NavItem(icon: Icons.grid_view_rounded, label: 'Kuyruk', path: '/admin'),
    _NavItem(icon: Icons.flag_rounded, label: 'Raporlar', path: '/admin/reports'),
    _NavItem(icon: Icons.people_rounded, label: 'Kullanıcılar', path: '/admin/users'),
    _NavItem(icon: Icons.article_rounded, label: 'Postlar', path: '/admin/posts'),
    _NavItem(icon: Icons.devices_rounded, label: 'Cihazlar', path: '/admin/devices'),
  ];

  @override
  Widget build(BuildContext context) {
    final location = GoRouter.of(context).routerDelegate.currentConfiguration.uri.path;
    final isDesktop = MediaQuery.sizeOf(context).width >= 720;

    if (isDesktop) {
      return Scaffold(
        backgroundColor: const Color(0xFF0F172A),
        body: Row(
          children: [
            _Sidebar(items: _items, location: location, adminService: adminService),
            Expanded(child: child),
          ],
        ),
      );
    }

    return Scaffold(
      backgroundColor: const Color(0xFF0F172A),
      body: child,
      bottomNavigationBar: NavigationBar(
        backgroundColor: const Color(0xFF1E293B),
        indicatorColor: AppColors.primary.withValues(alpha: 0.2),
        selectedIndex: _selectedIndex(location),
        onDestinationSelected: (i) => context.go(_items[i].path),
        destinations: _items
            .map((e) => NavigationDestination(
                  icon: Icon(e.icon, color: Colors.white54),
                  selectedIcon: Icon(e.icon, color: AppColors.primary),
                  label: e.label,
                ))
            .toList(),
      ),
    );
  }

  int _selectedIndex(String location) {
    for (var i = _items.length - 1; i >= 0; i--) {
      if (location.startsWith(_items[i].path)) return i;
    }
    return 0;
  }
}

class _Sidebar extends StatelessWidget {
  const _Sidebar({
    required this.items,
    required this.location,
    required this.adminService,
  });

  final List<_NavItem> items;
  final String location;
  final AdminService adminService;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 220,
      color: const Color(0xFF1E293B),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SizedBox(height: 40),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 20),
            child: Row(
              children: [
                const Icon(Icons.shield_rounded, color: AppColors.primary, size: 22),
                const SizedBox(width: 8),
                Text(
                  'Karar Admin',
                  style: Theme.of(context).textTheme.titleMedium?.copyWith(
                        color: Colors.white,
                        fontWeight: FontWeight.w800,
                      ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 32),
          ...items.map((item) {
            final selected = location == item.path ||
                (item.path != '/admin' && location.startsWith(item.path));
            return _SidebarItem(item: item, selected: selected);
          }),
          const Spacer(),
          Padding(
            padding: const EdgeInsets.all(16),
            child: TextButton.icon(
              onPressed: () {
                adminService.logout();
                context.go('/admin/login');
              },
              icon: const Icon(Icons.logout, size: 16, color: Colors.white54),
              label: const Text('Çıkış', style: TextStyle(color: Colors.white54)),
            ),
          ),
          const SizedBox(height: 16),
        ],
      ),
    );
  }
}

class _SidebarItem extends StatelessWidget {
  const _SidebarItem({required this.item, required this.selected});
  final _NavItem item;
  final bool selected;

  @override
  Widget build(BuildContext context) {
    return InkWell(
      onTap: () => context.go(item.path),
      child: Container(
        margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 2),
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        decoration: BoxDecoration(
          color: selected ? AppColors.primary.withValues(alpha: 0.15) : null,
          borderRadius: BorderRadius.circular(8),
        ),
        child: Row(
          children: [
            Icon(item.icon,
                size: 18,
                color: selected ? AppColors.primary : Colors.white54),
            const SizedBox(width: 10),
            Text(
              item.label,
              style: TextStyle(
                color: selected ? AppColors.primary : Colors.white70,
                fontWeight: selected ? FontWeight.w600 : FontWeight.normal,
                fontSize: 14,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _NavItem {
  const _NavItem({required this.icon, required this.label, required this.path});
  final IconData icon;
  final String label;
  final String path;
}
