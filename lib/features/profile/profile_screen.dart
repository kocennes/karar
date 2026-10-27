import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_service.dart';
import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/count_format.dart';
import '../../core/utils/date_formatter.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/karar_text_tabs.dart';
import '../settings/widgets/settings_group.dart';
import 'my_comments_screen.dart';
import 'my_posts_screen.dart';
import 'saved_posts_screen.dart';
import 'widgets/profile_header.dart';

class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(currentUserProvider);
    return user != null ? _LoggedInProfile(user: user) : const _GuestProfile();
  }
}

/// Tab-root app bar title (left aligned, `screenTitle`).
class _ProfileTitle extends StatelessWidget {
  const _ProfileTitle();

  @override
  Widget build(BuildContext context) {
    return Text(
      'Profil',
      style: AppTypography.screenTitle.copyWith(color: context.kc.text1),
    );
  }
}

class _LoggedInProfile extends ConsumerStatefulWidget {
  const _LoggedInProfile({required this.user});
  final AuthUser user;

  @override
  ConsumerState<_LoggedInProfile> createState() => _LoggedInProfileState();
}

class _LoggedInProfileState extends ConsumerState<_LoggedInProfile> {
  static const _tabs = ['Gönderiler', 'Yorumlar', 'Kaydedilenler'];
  int _tab = 0;

  @override
  Widget build(BuildContext context) {
    final user = widget.user;
    final authMethod = user.authProvider == 'google' ? 'Google' : 'E-posta';
    final metaLine = user.joinedAt != null
        ? '${DateFormatter.full(user.joinedAt!)} tarihinden beri üye · $authMethod'
        : authMethod;

    return Scaffold(
      appBar: AppBar(
        title: const _ProfileTitle(),
        centerTitle: false,
        actions: [
          IconButton(
            tooltip: 'Ayarlar',
            icon: const Icon(Icons.settings_rounded),
            onPressed: () => context.push('/settings'),
          ),
          IconButton(
            tooltip: 'Çıkış yap',
            icon: const Icon(Icons.logout_rounded),
            onPressed: () => _confirmLogout(context, ref),
          ),
        ],
      ),
      body: CenteredContent(
        child: CustomScrollView(
          slivers: [
            SliverToBoxAdapter(
              child: ProfileHeader(
                user: user,
                metaLine: metaLine,
                stats: [
                  ProfileStat(
                    value: formatCompactCount(user.karma),
                    label: 'Karma',
                    onTap: () => context.push('/profile/karma'),
                  ),
                  ProfileStat(
                    value: formatCompactCount(user.postCount),
                    label: 'Gönderi',
                    onTap: () => context.push('/profile/posts'),
                  ),
                  ProfileStat(
                    value: formatCompactCount(user.commentCount),
                    label: 'Yorum',
                    onTap: () => context.push('/profile/comments'),
                  ),
                ],
                actions: Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Flexible(
                      child: KararButton(
                        label: 'Profili Düzenle',
                        icon: Icons.edit_rounded,
                        expand: false,
                        onPressed: () =>
                            context.push('/profile/edit', extra: user),
                      ),
                    ),
                    const SizedBox(width: 12),
                    KararCircleButton(
                      icon: Icons.insights_rounded,
                      tooltip: 'Haftalık Vicdan Karnesi',
                      style: KararCircleStyle.muted,
                      size: 48,
                      onPressed: () => context.push('/profile/weekly'),
                    ),
                  ],
                ),
              ),
            ),
            SliverToBoxAdapter(
              child: KararTextTabs(
                labels: _tabs,
                index: _tab,
                padding: const EdgeInsets.fromLTRB(24, 22, 24, 4),
                onChanged: (i) => setState(() => _tab = i),
              ),
            ),
            switch (_tab) {
              1 => const MyCommentsList(
                  key: ValueKey('profile_tab_comments'),
                  sliver: true,
                ),
              2 => const SavedPostsGrid(
                  key: ValueKey('profile_tab_saved'),
                  sliver: true,
                ),
              _ => const MyPostsGrid(
                  key: ValueKey('profile_tab_posts'),
                  sliver: true,
                ),
            },
            SliverToBoxAdapter(
              child: SizedBox(height: context.navBottomInset + 24),
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
    final kc = context.kc;
    return Scaffold(
      appBar: AppBar(
        title: const _ProfileTitle(),
        centerTitle: false,
      ),
      body: CenteredContent(
        child: ListView(
          padding: EdgeInsets.fromLTRB(0, 8, 0, context.navBottomInset + 24),
          children: [
            const Center(
              child: KararAvatar(username: '', radius: 48, anonymous: true),
            ),
            const SizedBox(height: 16),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 24),
              child: Text(
                'Misafir olarak kullanıyorsun',
                textAlign: TextAlign.center,
                style: AppTypography.headline.copyWith(
                  fontSize: 22,
                  height: 28 / 22,
                  color: kc.text1,
                ),
              ),
            ),
            const SizedBox(height: 8),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 32),
              child: Text(
                'Karma ve bildirim takibi için hesap aç. Oy vermeye ve yorum yapmaya hemen başlayabilirsin.',
                textAlign: TextAlign.center,
                style: AppTypography.body.copyWith(color: kc.text2),
              ),
            ),
            const SizedBox(height: 20),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 24),
              child: Row(
                children: [
                  Expanded(
                    child: KararButton(
                      label: 'Hesap aç',
                      onPressed: () => context.push('/auth/register'),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: KararButton(
                      label: 'Giriş yap',
                      variant: KararButtonVariant.outlined,
                      onPressed: () => context.push('/auth/login'),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 28),
            const SettingsGroup(
              children: [
                SettingsRow(
                  icon: Icons.how_to_vote_rounded,
                  title: 'Verdiğin oylar',
                  value: '0',
                ),
                SettingsRow(
                  icon: Icons.chat_bubble_outline_rounded,
                  title: 'Yorumların',
                  value: '0',
                ),
                SettingsRow(
                  icon: Icons.auto_awesome_rounded,
                  title: 'Karma',
                  value: 'Hesap açınca aktif',
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
