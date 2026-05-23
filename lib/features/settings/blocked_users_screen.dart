import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_service.dart';
import '../../core/providers.dart';
import '../../core/utils/date_formatter.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../../shared/widgets/centered_content.dart';

class BlockedUsersScreen extends ConsumerStatefulWidget {
  const BlockedUsersScreen({super.key});

  @override
  ConsumerState<BlockedUsersScreen> createState() => _BlockedUsersScreenState();
}

class _BlockedUsersScreenState extends ConsumerState<BlockedUsersScreen> {
  List<BlockedUser>? _users;
  bool _isLoading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      final users = await ref.read(authServiceProvider).fetchBlockedUsers();
      if (!mounted) return;
      setState(() {
        _users = users;
        _isLoading = false;
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _error = 'Engellenen kullanıcılar yüklenemedi.';
        _isLoading = false;
      });
    }
  }

  Future<void> _unblock(BlockedUser user) async {
    try {
      await ref.read(authServiceProvider).unblockUser(user.id);
      if (!mounted) return;
      setState(() {
        _users = [...?_users]..removeWhere((u) => u.id == user.id);
      });
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('@${user.username} engeli kaldırıldı.')),
      );
    } catch (_) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Engel kaldırılamadı.')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Engellenenler'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: _buildBody(),
      ),
    );
  }

  Widget _buildBody() {
    if (_isLoading) return const LoadingIndicator();
    if (_error != null) return ErrorView(message: _error!, onRetry: _load);
    final users = _users ?? const <BlockedUser>[];
    if (users.isEmpty) {
      return const EmptyState(
        message: 'Engellenen kullanıcı yok.',
        icon: Icons.block_outlined,
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.separated(
        padding: const EdgeInsets.symmetric(vertical: 12),
        itemCount: users.length,
        separatorBuilder: (_, __) => const Divider(height: 1),
        itemBuilder: (context, index) {
          final user = users[index];
          return ListTile(
            leading: CircleAvatar(
              child: Text(
                  user.username.isEmpty ? '?' : user.username[0].toUpperCase()),
            ),
            title: Text(
              '@${user.username}',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
            subtitle: Text(
              'Engellendi: ${DateFormatter.full(user.blockedAt)}',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
            trailing: TextButton.icon(
              onPressed: () => _confirmUnblock(user),
              icon: const Icon(Icons.lock_open_outlined),
              label: const Text('Kaldır'),
            ),
          );
        },
      ),
    );
  }

  void _confirmUnblock(BlockedUser user) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Engeli kaldır?'),
        content: Text('@${user.username} paylaşımlarını tekrar görebilirsin.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            onPressed: () {
              Navigator.pop(ctx);
              _unblock(user);
            },
            child: const Text('Kaldır'),
          ),
        ],
      ),
    );
  }
}

