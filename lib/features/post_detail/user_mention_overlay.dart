import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_surface.dart';

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

    final kc = context.kc;

    return KararSurface(
      radius: KararRadius.input,
      padding: const EdgeInsets.symmetric(vertical: 6),
      color: kc.elevated,
      shadow: KararShadowKind.float,
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxHeight: 200, maxWidth: 260),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (_isLoading)
              LinearProgressIndicator(
                minHeight: 2,
                color: kc.primary,
                backgroundColor: kc.muted,
              ),
            Flexible(
              child: ListView.builder(
                shrinkWrap: true,
                padding: EdgeInsets.zero,
                itemCount: _users.length,
                itemBuilder: (context, index) {
                  final user = _users[index];
                  final username = user['username'] as String;
                  return ListTile(
                    minTileHeight: 48,
                    contentPadding: const EdgeInsets.symmetric(horizontal: 12),
                    leading: KararAvatar(
                      username: username,
                      radius: 14,
                      fontSize: 11,
                      shape: KararAvatarShape.rounded,
                    ),
                    title: Text(
                      '@$username',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: AppTypography.bodyStrong.copyWith(color: kc.text1),
                    ),
                    onTap: () => widget.onSelect(username),
                  );
                },
              ),
            ),
            if (_users.isEmpty && _isLoading)
              Padding(
                padding: const EdgeInsets.all(16),
                child: Text(
                  'Aranıyor...',
                  style: AppTypography.meta.copyWith(color: kc.text3),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
