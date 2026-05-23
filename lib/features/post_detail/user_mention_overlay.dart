import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import '../../core/providers.dart';
import '../../shared/widgets/karar_avatar.dart';

class UserMentionOverlay extends ConsumerStatefulWidget {
  const UserMentionOverlay({
    super.key,
    required this.query,
    required this.onSelect,
  });

  final String query;
  final ValueChanged<String> onSelect;

  @override
  ConsumerState<UserMentionOverlay> createState() => _UserMentionOverlayState();
}

class _UserMentionOverlayState extends ConsumerState<UserMentionOverlay> {
  List<Map<String, Object?>> _users = [];
  bool _isLoading = false;

  @override
  void initState() {
    super.initState();
    _fetchUsers();
  }

  @override
  void didUpdateWidget(UserMentionOverlay oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.query != widget.query) {
      _fetchUsers();
    }
  }

  Future<void> _fetchUsers() async {
    if (widget.query.length < 2) {
      setState(() => _users = []);
      return;
    }

    setState(() => _isLoading = true);
    try {
      final results = await ref.read(postRepositoryProvider).searchUsers(widget.query);
      if (mounted) {
        setState(() {
          _users = results;
          _isLoading = false;
        });
      }
    } catch (_) {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_users.isEmpty && !_isLoading) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return Material(
      elevation: 8,
      borderRadius: BorderRadius.circular(12),
      clipBehavior: Clip.antiAlias,
      color: colorScheme.surface,
      child: Container(
        constraints: const BoxConstraints(maxHeight: 200, maxWidth: 260),
        decoration: BoxDecoration(
          border: Border.all(color: colorScheme.outlineVariant),
          borderRadius: BorderRadius.circular(12),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (_isLoading)
              const LinearProgressIndicator(minHeight: 2),
            Flexible(
              child: ListView.builder(
                shrinkWrap: true,
                padding: EdgeInsets.zero,
                itemCount: _users.length,
                itemBuilder: (context, index) {
                  final user = _users[index];
                  final username = user['username'] as String;
                  return ListTile(
                    dense: true,
                    leading: KararAvatar(username: username, radius: 14, fontSize: 10),
                    title: Text('@$username', style: const TextStyle(fontWeight: FontWeight.bold)),
                    onTap: () => widget.onSelect(username),
                  );
                },
              ),
            ),
            if (_users.isEmpty && _isLoading)
              const Padding(
                padding: EdgeInsets.all(16),
                child: Text('Aranıyor...', style: TextStyle(fontSize: 12)),
              ),
          ],
        ),
      ),
    );
  }
}
