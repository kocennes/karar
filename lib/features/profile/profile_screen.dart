import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_service.dart';
import '../../core/providers.dart';
import '../../core/utils/date_formatter.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karma_badge.dart';
import '../../shared/widgets/centered_content.dart';

class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(currentUserProvider);
    return user != null ? _LoggedInProfile(user: user) : const _GuestProfile();
  }
}

class _LoggedInProfile extends ConsumerWidget {
  const _LoggedInProfile({required this.user});
  final AuthUser user;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Profil'),
        centerTitle: true,
        actions: [
          IconButton(
            tooltip: 'Profili Düzenle',
            icon: const Icon(Icons.edit_outlined),
            onPressed: () => context.push('/profile/edit', extra: user),
          ),
          IconButton(
            tooltip: 'Ayarlar',
            icon: const Icon(Icons.settings_outlined),
            onPressed: () => context.push('/settings'),
          ),
          IconButton(
            tooltip: 'Çıkış yap',
            icon: const Icon(Icons.logout),
            onPressed: () => _confirmLogout(context, ref),
          ),
        ],
      ),
      body: CenteredContent(
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Card(
              child: Padding(
                padding: const EdgeInsets.all(18),
                child: Row(
                  children: [
                    KararAvatar(
                      username: user.username,
                      radius: 28,
                      fontSize: 22,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Flexible(
                                child: Text(
                                  user.username,
                                  style: Theme.of(context)
                                      .textTheme
                                      .titleLarge
                                      ?.copyWith(fontWeight: FontWeight.w800),
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                              const SizedBox(width: 6),
                              KarmaBadge(karma: user.karma, showDetail: true),
                            ],
                          ),
                          Text(
                            user.email,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style:
                                Theme.of(context).textTheme.bodySmall?.copyWith(
                                      color: Theme.of(context)
                                          .colorScheme
                                          .onSurfaceVariant,
                                    ),
                          ),
                          if ((user.bio ?? '').isNotEmpty) ...[
                            const SizedBox(height: 6),
                            Text(
                              user.bio!,
                              maxLines: 3,
                              overflow: TextOverflow.ellipsis,
                              style: Theme.of(context).textTheme.bodyMedium,
                            ),
                          ],
                        ],
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 12),
            _MetricTile(
              icon: Icons.auto_awesome_outlined,
              title: 'Karma',
              value: '${user.karma}',
              onTap: () => context.push('/profile/karma'),
              trailing: const Icon(Icons.chevron_right),
            ),
            const SizedBox(height: 4),
            _MetricTile(
              icon: Icons.calendar_view_week_outlined,
              title: 'Haftalık Vicdan Karnesi',
              value: '',
              trailing: const Icon(Icons.chevron_right),
              onTap: () => context.push('/profile/weekly'),
            ),
            const SizedBox(height: 12),
            _MetricTile(
              icon: Icons.article_outlined,
              title: 'Gönderilerim',
              value: '${user.postCount}',
              trailing: const Icon(Icons.chevron_right),
              onTap: () => context.push('/profile/posts'),
            ),
            const SizedBox(height: 4),
            _MetricTile(
              icon: Icons.bookmark_border_outlined,
              title: 'Kaydedilenler',
              value: '',
              trailing: const Icon(Icons.chevron_right),
              onTap: () => context.push('/profile/saved'),
            ),
            const SizedBox(height: 4),
            _MetricTile(
              icon: Icons.chat_bubble_outline,
              title: 'Yorumlarım',
              value: '${user.commentCount}',
              trailing: const Icon(Icons.chevron_right),
              onTap: () => context.push('/profile/comments'),
            ),
            const SizedBox(height: 4),
            if (user.joinedAt != null) ...[
              _MetricTile(
                icon: Icons.calendar_today_outlined,
                title: 'Üyelik',
                value: DateFormatter.full(user.joinedAt!),
              ),
              const SizedBox(height: 4),
            ],
            _MetricTile(
              icon: Icons.verified_user_outlined,
              title: 'Giriş yöntemi',
              value: user.authProvider == 'google' ? 'Google' : 'E-posta',
            ),
          ],
        ),
      ),
    );
  }

  void _confirmLogout(BuildContext context, WidgetRef ref) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Çıkış yap'),
        content: const Text('Hesabından çıkmak istiyor musun?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('İptal'),
          ),
          FilledButton(
            onPressed: () async {
              Navigator.pop(ctx);
              await ref.read(notificationServiceProvider).deleteCurrentToken();
              final authService = ref.read(authServiceProvider);
              await authService.logout();
              ref.read(currentUserProvider.notifier).state = null;
            },
            child: const Text('Çıkış yap'),
          ),
        ],
      ),
    );
  }
}

class _GuestProfile extends StatelessWidget {
  const _GuestProfile();

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Profil'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Card(
              child: Padding(
                padding: const EdgeInsets.all(18),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Misafir olarak kullanıyorsun',
                      style: Theme.of(context).textTheme.titleLarge?.copyWith(
                            fontWeight: FontWeight.w800,
                          ),
                    ),
                    const SizedBox(height: 8),
                    const Text(
                      'Karma ve bildirim takibi için hesap aç. Oy vermeye ve yorum yapmaya hemen başlayabilirsin.',
                    ),
                    const SizedBox(height: 16),
                    Row(
                      children: [
                        Expanded(
                          child: FilledButton(
                            onPressed: () => context.push('/auth/register'),
                            child: const Text('Hesap aç'),
                          ),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: OutlinedButton(
                            onPressed: () => context.push('/auth/login'),
                            child: const Text('Giriş yap'),
                          ),
                        ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 12),
            const _MetricTile(
              icon: Icons.how_to_vote_outlined,
              title: 'Verdiğin oylar',
              value: '0',
            ),
            const _MetricTile(
              icon: Icons.chat_bubble_outline,
              title: 'Yorumların',
              value: '0',
            ),
            const _MetricTile(
              icon: Icons.auto_awesome_outlined,
              title: 'Karma',
              value: 'Hesap açınca aktif',
            ),
          ],
        ),
      ),
    );
  }
}

class _MetricTile extends StatelessWidget {
  const _MetricTile({
    required this.icon,
    required this.title,
    required this.value,
    this.trailing,
    this.onTap,
  });

  final IconData icon;
  final String title;
  final String value;
  final Widget? trailing;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: ListTile(
        leading: Icon(icon),
        title: Text(
          title,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        trailing: trailing ??
            (value.isNotEmpty
                ? ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 160),
                    child: Text(
                      value,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      textAlign: TextAlign.end,
                      style: Theme.of(context).textTheme.labelLarge?.copyWith(
                            fontWeight: FontWeight.w800,
                          ),
                    ),
                  )
                : null),
        onTap: onTap,
      ),
    );
  }
}
