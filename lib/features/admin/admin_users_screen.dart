import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import 'admin_service.dart';

class AdminUsersScreen extends StatefulWidget {
  const AdminUsersScreen({super.key, required this.adminService});
  final AdminService adminService;

  @override
  State<AdminUsersScreen> createState() => _AdminUsersScreenState();
}

class _AdminUsersScreenState extends State<AdminUsersScreen> {
  List<AdminUser> _items = [];
  bool _loading = true;
  String? _error;
  final _searchCtrl = TextEditingController();

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _searchCtrl.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final q = _searchCtrl.text.trim();
      final result = await widget.adminService.fetchUsers(
          search: q.isEmpty ? null : q);
      setState(() => _items = result.items);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  void _ban(AdminUser user) {
    showDialog<void>(
      context: context,
      builder: (_) => _BanDialog(
        username: user.username,
        onConfirm: (reason) async {
          await widget.adminService.banUser(user.id, reason: reason);
          _load();
        },
      ),
    );
  }

  Future<void> _unban(AdminUser user) async {
    await widget.adminService.unbanUser(user.id);
    _load();
  }

  void _warn(AdminUser user) {
    showDialog<void>(
      context: context,
      builder: (_) => _WarnDialog(
        username: user.username,
        onConfirm: (msg) async {
          await widget.adminService.warnUser(user.id, message: msg);
        },
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0F172A),
      appBar: AppBar(
        backgroundColor: const Color(0xFF1E293B),
        title: const Text('Kullanıcılar', style: TextStyle(color: Colors.white)),
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: TextField(
              controller: _searchCtrl,
              style: const TextStyle(color: Colors.white),
              decoration: InputDecoration(
                hintText: 'Kullanıcı adı veya e-posta ara...',
                hintStyle: const TextStyle(color: Colors.white38),
                prefixIcon: const Icon(Icons.search, color: Colors.white38),
                filled: true,
                fillColor: const Color(0xFF1E293B),
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(10),
                  borderSide: BorderSide.none,
                ),
                suffixIcon: IconButton(
                  icon: const Icon(Icons.arrow_forward, color: Colors.white54),
                  onPressed: _load,
                ),
              ),
              onSubmitted: (_) => _load(),
            ),
          ),
          Expanded(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _error != null
                    ? Center(child: Text(_error!,
                        style: const TextStyle(color: Colors.white54)))
                    : _items.isEmpty
                        ? const Center(child: Text('Kullanıcı bulunamadı.',
                            style: TextStyle(color: Colors.white54)))
                        : ListView.separated(
                            padding: const EdgeInsets.symmetric(horizontal: 16),
                            itemCount: _items.length,
                            separatorBuilder: (_, __) =>
                                const SizedBox(height: 8),
                            itemBuilder: (_, i) => _UserRow(
                              user: _items[i],
                              onBan: () => _ban(_items[i]),
                              onUnban: () => _unban(_items[i]),
                              onWarn: () => _warn(_items[i]),
                            ),
                          ),
          ),
        ],
      ),
    );
  }
}

class _UserRow extends StatelessWidget {
  const _UserRow({
    required this.user,
    required this.onBan,
    required this.onUnban,
    required this.onWarn,
  });

  final AdminUser user;
  final VoidCallback onBan;
  final VoidCallback onUnban;
  final VoidCallback onWarn;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: const Color(0xFF1E293B),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(
          color: user.isBanned
              ? AppColors.haksiz.withValues(alpha: 0.3)
              : Colors.white.withValues(alpha: 0.07),
        ),
      ),
      child: Row(
        children: [
          CircleAvatar(
            radius: 18,
            backgroundColor: AppColors.primary.withValues(alpha: 0.2),
            child: Text(
              user.username.isNotEmpty ? user.username[0].toUpperCase() : '?',
              style: const TextStyle(color: AppColors.primary, fontWeight: FontWeight.w700),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Text('@${user.username}',
                        style: const TextStyle(
                            color: Colors.white, fontWeight: FontWeight.w600)),
                    if (user.isBanned) ...[
                      const SizedBox(width: 6),
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                        decoration: BoxDecoration(
                          color: AppColors.haksiz.withValues(alpha: 0.15),
                          borderRadius: BorderRadius.circular(4),
                        ),
                        child: const Text('BANLANDI',
                            style: TextStyle(
                                color: AppColors.haksiz,
                                fontSize: 10,
                                fontWeight: FontWeight.w700)),
                      ),
                    ],
                  ],
                ),
                Text(user.email,
                    style: const TextStyle(color: Colors.white54, fontSize: 12)),
                Text('${user.postCount} post · ${user.commentCount} yorum',
                    style: const TextStyle(color: Colors.white38, fontSize: 11)),
              ],
            ),
          ),
          PopupMenuButton<String>(
            color: const Color(0xFF1E293B),
            icon: const Icon(Icons.more_vert, color: Colors.white54),
            onSelected: (v) {
              if (v == 'ban') onBan();
              if (v == 'unban') onUnban();
              if (v == 'warn') onWarn();
            },
            itemBuilder: (_) => [
              if (!user.isBanned)
                const PopupMenuItem(
                  value: 'warn',
                  child: Text('Uyar', style: TextStyle(color: Colors.white)),
                ),
              if (!user.isBanned)
                const PopupMenuItem(
                  value: 'ban',
                  child: Text('Banla', style: TextStyle(color: AppColors.haksiz)),
                ),
              if (user.isBanned)
                const PopupMenuItem(
                  value: 'unban',
                  child: Text('Banı Kaldır', style: TextStyle(color: AppColors.hakli)),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _BanDialog extends StatefulWidget {
  const _BanDialog({required this.username, required this.onConfirm});
  final String username;
  final Future<void> Function(String reason) onConfirm;

  @override
  State<_BanDialog> createState() => _BanDialogState();
}

class _BanDialogState extends State<_BanDialog> {
  final _ctrl = TextEditingController();
  bool _loading = false;

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      backgroundColor: const Color(0xFF1E293B),
      title: Text('@${widget.username} banla',
          style: const TextStyle(color: Colors.white)),
      content: TextField(
        controller: _ctrl,
        style: const TextStyle(color: Colors.white),
        decoration: const InputDecoration(
          hintText: 'Ban sebebi',
          hintStyle: TextStyle(color: Colors.white38),
          enabledBorder: UnderlineInputBorder(
              borderSide: BorderSide(color: Colors.white24)),
        ),
      ),
      actions: [
        TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('İptal', style: TextStyle(color: Colors.white54))),
        FilledButton(
          style: FilledButton.styleFrom(backgroundColor: AppColors.haksiz),
          onPressed: _loading
              ? null
              : () async {
                  setState(() => _loading = true);
                  await widget.onConfirm(_ctrl.text.trim());
                  if (context.mounted) Navigator.pop(context);
                },
          child: const Text('Banla'),
        ),
      ],
    );
  }
}

class _WarnDialog extends StatefulWidget {
  const _WarnDialog({required this.username, required this.onConfirm});
  final String username;
  final Future<void> Function(String msg) onConfirm;

  @override
  State<_WarnDialog> createState() => _WarnDialogState();
}

class _WarnDialogState extends State<_WarnDialog> {
  final _ctrl = TextEditingController();
  bool _loading = false;

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      backgroundColor: const Color(0xFF1E293B),
      title: Text('@${widget.username} uyar',
          style: const TextStyle(color: Colors.white)),
      content: TextField(
        controller: _ctrl,
        style: const TextStyle(color: Colors.white),
        maxLines: 3,
        decoration: const InputDecoration(
          hintText: 'Uyarı mesajı',
          hintStyle: TextStyle(color: Colors.white38),
          enabledBorder: UnderlineInputBorder(
              borderSide: BorderSide(color: Colors.white24)),
        ),
      ),
      actions: [
        TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('İptal', style: TextStyle(color: Colors.white54))),
        FilledButton(
          style: FilledButton.styleFrom(backgroundColor: Colors.orange),
          onPressed: _loading
              ? null
              : () async {
                  setState(() => _loading = true);
                  await widget.onConfirm(_ctrl.text.trim());
                  if (context.mounted) Navigator.pop(context);
                },
          child: const Text('Uyar'),
        ),
      ],
    );
  }
}
