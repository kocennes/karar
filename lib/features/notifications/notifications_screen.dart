import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';
import '../../core/notifications/notification_deep_link.dart';
import '../../core/settings/preferences_provider.dart';
import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/loading_indicator.dart';
import 'data/notification_item.dart';
import 'notifications_provider.dart';

class NotificationsScreen extends ConsumerWidget {
  const NotificationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(currentUserProvider);
    final state = ref.watch(notificationsProvider);

    return Scaffold(
      appBar: AppBar(
        title: Text(
          state.unreadCount > 0
              ? 'Bildirimler (${state.unreadCount})'
              : 'Bildirimler',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          if (state.unreadCount > 0)
            TextButton(
              onPressed: () =>
                  ref.read(notificationsProvider.notifier).markAllRead(),
              child: const Text('Tümünü okundu say'),
            ),
          if (state.hasReadItems)
            IconButton(
              icon: const Icon(Icons.cleaning_services_outlined),
              tooltip: 'Okunmuşları temizle',
              onPressed: () =>
                  ref.read(notificationsProvider.notifier).clearRead(),
            ),
        ],
      ),
      body: CenteredContent(
        child: user == null
            ? const _GuestNotificationsView()
            : _buildBody(context, ref, state),
      ),
    );
  }

  Widget _buildBody(
    BuildContext context,
    WidgetRef ref,
    NotificationsState state,
  ) {
    if (state.isLoading) return const LoadingIndicator();

    if (state.error != null) {
      return ErrorView(
        message: state.error!,
        onRetry: () => ref.read(notificationsProvider.notifier).load(),
      );
    }

    const webPushBanner = kIsWeb ? _WebPushBanner() : SizedBox.shrink();

    if (state.items.isEmpty) {
      return const Column(
        children: [
          webPushBanner,
          _NotificationControls(),
          _DigestWidget(),
          Expanded(
            child: EmptyState(
              message:
                  'Henüz bildirim yok. Topluluk karar verdikçe burada göreceksin.',
              icon: Icons.notifications_none_outlined,
            ),
          ),
        ],
      );
    }

    return Column(
      children: [
        webPushBanner,
        const _NotificationControls(),
        Expanded(
          child: RefreshIndicator(
            onRefresh: () => ref.read(notificationsProvider.notifier).load(),
            child: ListView.builder(
              padding: const EdgeInsets.symmetric(vertical: 8),
              itemCount: state.items.length + 1,
              itemBuilder: (context, index) {
                if (index == 0) return const _DigestWidget();
                return _NotificationTile(item: state.items[index - 1]);
              },
            ),
          ),
        ),
      ],
    );
  }
}

class _NotificationControls extends ConsumerWidget {
  const _NotificationControls();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final prefs = ref.watch(userPreferencesProvider);

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 2),
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(
          children: [
            ActionChip(
              avatar: const Icon(Icons.notifications_active_outlined, size: 18),
              label:
                  Text(prefs.pushEnabled ? 'İzin açık' : 'Bildirim izni ver'),
              onPressed: () => _requestPermission(context, ref),
            ),
            const SizedBox(width: 8),
            ActionChip(
              avatar: const Icon(Icons.volume_up_outlined, size: 18),
              label:
                  Text(prefs.soundEnabled ? 'Ses açık' : 'Bildirim sesini aç'),
              onPressed: () => _enableSound(context, ref),
            ),
            const SizedBox(width: 8),
            ActionChip(
              avatar: const Icon(Icons.notifications_paused_outlined, size: 18),
              label: const Text('Sessize al'),
              onPressed: () => _showMuteSheet(context, ref),
            ),
            const SizedBox(width: 8),
            ActionChip(
              avatar: const Icon(Icons.tune_outlined, size: 18),
              label: const Text('Tercihleri yönet'),
              onPressed: () => context.push('/settings/notifications'),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _requestPermission(BuildContext context, WidgetRef ref) async {
    await ref
        .read(userPreferencesProvider.notifier)
        .update((s) => s.copyWith(pushEnabled: true));
    final notifications = ref.read(notificationServiceProvider);
    await notifications.maybeRequestPermission(force: true);
    final denied = await notifications.isDenied();
    if (!context.mounted || !denied) return;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(notifications.deniedPermissionHelpText),
        action: notifications.canOpenPlatformNotificationSettings
            ? SnackBarAction(
                label: 'Ayarlar',
                onPressed: () => notifications.openSettings(),
              )
            : null,
      ),
    );
  }

  Future<void> _enableSound(BuildContext context, WidgetRef ref) async {
    await ref
        .read(userPreferencesProvider.notifier)
        .update((s) => s.copyWith(soundEnabled: true, pushEnabled: true));
    await ref.read(notificationServiceProvider).openSettings();
  }

  Future<void> _showMuteSheet(BuildContext context, WidgetRef ref) async {
    final duration = await showModalBottomSheet<String>(
      context: context,
      builder: (ctx) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const SizedBox(height: 8),
            ListTile(
              leading: const Icon(Icons.schedule_outlined),
              title: const Text('1 saat'),
              onTap: () => Navigator.pop(ctx, '1h'),
            ),
            ListTile(
              leading: const Icon(Icons.today_outlined),
              title: const Text('Bugün'),
              onTap: () => Navigator.pop(ctx, 'today'),
            ),
            ListTile(
              leading: const Icon(Icons.date_range_outlined),
              title: const Text('7 gün'),
              onTap: () => Navigator.pop(ctx, '7d'),
            ),
            ListTile(
              leading: const Icon(Icons.notifications_off_outlined),
              title: const Text('Süresiz'),
              onTap: () => Navigator.pop(ctx, 'indefinite'),
            ),
            const SizedBox(height: 8),
          ],
        ),
      ),
    );
    if (duration == null || !context.mounted) return;

    final success =
        await ref.read(notificationsProvider.notifier).mute(duration);
    if (!context.mounted) return;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          success
              ? 'Bildirimler sessize alındı.'
              : 'Sessize alma kaydedilemedi. Tekrar dene.',
        ),
      ),
    );
  }
}

class _WebPushBanner extends ConsumerStatefulWidget {
  const _WebPushBanner();

  @override
  ConsumerState<_WebPushBanner> createState() => _WebPushBannerState();
}

class _WebPushBannerState extends ConsumerState<_WebPushBanner> {
  AuthorizationStatus? _status;
  bool _isRequesting = false;
  bool _onCooldown = false;

  @override
  void initState() {
    super.initState();
    _checkStatus();
  }

  Future<void> _checkStatus() async {
    final notifService = ref.read(notificationServiceProvider);
    final results = await Future.wait([
      FirebaseMessaging.instance.getNotificationSettings(),
      notifService.isSoftPromptOnCooldown(),
    ]);
    if (!mounted) return;
    final settings = results[0] as NotificationSettings;
    final onCooldown = results[1] as bool;
    setState(() {
      _status = settings.authorizationStatus;
      _onCooldown = onCooldown;
    });
  }

  Future<void> _requestPermission() async {
    final notifService = ref.read(notificationServiceProvider);
    final analytics = ref.read(analyticsServiceProvider);

    await analytics.logNotificationPermissionPromptShown(
      surface: 'web_notifications_tab',
      trigger: 'manual_tap',
    );

    final shouldRequest = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        icon: const Text('🔔', style: TextStyle(fontSize: 40)),
        title: const Text(
          'Bildirimleri Aç',
          textAlign: TextAlign.center,
        ),
        content: const Text(
          'Postun oylanınca veya topluluk karar verince seni haberdar edelim.',
          textAlign: TextAlign.center,
        ),
        actionsAlignment: MainAxisAlignment.center,
        actions: [
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('İzin Ver'),
          ),
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Şimdilik değil'),
          ),
        ],
      ),
    );

    if (!mounted) return;

    if (shouldRequest != true) {
      await notifService.recordSoftPromptDismissed();
      await analytics.logNotificationPermissionDenied(source: 'soft_dismiss');
      setState(() => _onCooldown = true);
      return;
    }

    setState(() => _isRequesting = true);
    await notifService.maybeRequestPermission(force: true);
    if (!mounted) return;
    await _checkStatus();
    setState(() => _isRequesting = false);
  }

  @override
  Widget build(BuildContext context) {
    final status = _status;
    if (status == null) return const SizedBox.shrink();
    if (status == AuthorizationStatus.authorized ||
        status == AuthorizationStatus.provisional) {
      return const SizedBox.shrink();
    }
    if (_onCooldown && status != AuthorizationStatus.denied) {
      return const SizedBox.shrink();
    }

    final scheme = Theme.of(context).colorScheme;

    if (status == AuthorizationStatus.denied) {
      return Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
        child: Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: scheme.errorContainer.withValues(alpha: 0.4),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: scheme.error.withValues(alpha: 0.3)),
          ),
          child: Row(
            children: [
              Icon(Icons.notifications_off_outlined, color: scheme.error),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  'Bildirimler kapalı. Tarayıcı adres çubuğundaki kilit simgesinden bildirimlere izin verebilirsin.',
                  style:
                      TextStyle(fontSize: 13, color: scheme.onErrorContainer),
                ),
              ),
            ],
          ),
        ),
      );
    }

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
      child: Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Row(
            children: [
              const Text('🔔', style: TextStyle(fontSize: 24)),
              const SizedBox(width: 12),
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      'Bildirimleri Aç',
                      style: TextStyle(fontWeight: FontWeight.bold),
                    ),
                    Text(
                      'Topluluk karar verince haber ol.',
                      style: TextStyle(fontSize: 12),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 8),
              FilledButton(
                onPressed: _isRequesting ? null : _requestPermission,
                style: FilledButton.styleFrom(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  minimumSize: const Size(64, 36),
                  tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                ),
                child: _isRequesting
                    ? const SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(
                          strokeWidth: 2,
                          color: Colors.white,
                        ),
                      )
                    : const Text('Aç'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _GuestNotificationsView extends StatelessWidget {
  const _GuestNotificationsView();

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 32),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const Icon(
            Icons.notifications_outlined,
            size: 64,
            color: Colors.grey,
          ),
          const SizedBox(height: 24),
          Text(
            'Bildirim almak için hesap aç',
            style: Theme.of(context).textTheme.titleLarge?.copyWith(
                  fontWeight: FontWeight.bold,
                ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 12),
          Text(
            'Postun oylanınca veya topluluk karar verince seni haberdar edelim.',
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 32),
          KararButton(
            label: 'Hesap Aç',
            onPressed: () => context.push('/auth/register'),
          ),
          const SizedBox(height: 12),
          KararButton(
            label: 'Giriş Yap',
            variant: KararButtonVariant.outlined,
            onPressed: () => context.push('/auth/login'),
          ),
        ],
      ),
    );
  }
}

class _DigestWidget extends ConsumerWidget {
  const _DigestWidget();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(digestPostsProvider);

    return async.when(
      loading: () => const SizedBox.shrink(),
      error: (_, __) => const SizedBox.shrink(),
      data: (posts) {
        if (posts.isEmpty) return const SizedBox.shrink();
        final scheme = Theme.of(context).colorScheme;
        return Padding(
          padding: const EdgeInsets.fromLTRB(16, 10, 16, 2),
          child: Card(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 10),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      const Text('📊', style: TextStyle(fontSize: 16)),
                      const SizedBox(width: 8),
                      Text(
                        'Dün Kaçırdıkların',
                        style: Theme.of(context).textTheme.titleSmall?.copyWith(
                              fontWeight: FontWeight.w700,
                            ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  ...posts.map((p) => _DigestItem(post: p)),
                  const SizedBox(height: 6),
                  Align(
                    alignment: Alignment.centerRight,
                    child: TextButton(
                      style: TextButton.styleFrom(
                        padding: EdgeInsets.zero,
                        minimumSize: const Size(44, 32),
                        tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                      ),
                      onPressed: () => context.go('/'),
                      child: Text(
                        'Hepsini Gör →',
                        style: TextStyle(
                          fontSize: 13,
                          color: scheme.primary,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }
}

class _DigestItem extends StatelessWidget {
  const _DigestItem({required this.post});
  final Post post;

  @override
  Widget build(BuildContext context) {
    final total = post.totalVotes;
    final votesLabel = total >= 1000
        ? '${(total / 1000).toStringAsFixed(1)}B oy'
        : '$total oy';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: GestureDetector(
        onTap: () => context.push('/posts/${post.id}', extra: post),
        child: Row(
          children: [
            const Text('•  ', style: TextStyle(fontWeight: FontWeight.w700)),
            Expanded(
              child: Text(
                post.title,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ),
            const SizedBox(width: 6),
            Text(
              '→ $votesLabel',
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    fontWeight: FontWeight.w600,
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
            ),
          ],
        ),
      ),
    );
  }
}

class _NotificationTile extends ConsumerWidget {
  const _NotificationTile({required this.item});
  final NotificationItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colorScheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;
    final notifier = ref.read(notificationsProvider.notifier);

    return Dismissible(
      key: ValueKey(item.id),
      direction: DismissDirection.endToStart,
      background: Container(
        alignment: Alignment.centerRight,
        padding: const EdgeInsets.only(right: 20),
        color: colorScheme.errorContainer,
        child: Icon(Icons.delete_outline, color: colorScheme.onErrorContainer),
      ),
      onDismissed: (_) {
        ref.read(analyticsServiceProvider).logNotificationDismissed(
              notificationId: item.id,
              type: item.type,
            );
        notifier.dismiss(item.id);
      },
      child: ListTile(
        tileColor: item.isRead
            ? null
            : colorScheme.primaryContainer.withValues(alpha: 0.15),
        leading: Icon(
          _iconForType(item.type),
          color:
              item.isRead ? colorScheme.onSurfaceVariant : colorScheme.primary,
        ),
        title: Text(
          item.title,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: textTheme.bodyMedium?.copyWith(
            fontWeight: item.isRead ? FontWeight.normal : FontWeight.w700,
          ),
        ),
        subtitle: _NotificationSubtitle(item: item),
        trailing: !item.isRead
            ? Icon(Icons.circle, size: 8, color: colorScheme.primary)
            : null,
        onTap: () {
          ref.read(analyticsServiceProvider).logNotificationOpened(
                notificationId: item.id,
                type: item.type,
              );
          notifier.markOpened(item.id);
          if (!item.isRead) {
            ref.read(analyticsServiceProvider).logNotificationMarkedRead(
                  notificationId: item.id,
                  type: item.type,
                );
            notifier.markRead(item.id);
          }
          _navigate(context, item);
        },
      ),
    );
  }

  void _navigate(BuildContext context, NotificationItem item) {
    context.push(
      NotificationDeepLink.fromNotificationItem(
        deepLink: item.deepLink,
        postId: item.postId,
      ),
    );
  }

  IconData _iconForType(String type) => switch (type) {
        'verdict_milestone' => Icons.emoji_events_outlined,
        'verdict_reminder' => Icons.how_to_vote_outlined,
        'comment_on_post' => Icons.chat_bubble_outline,
        'reply_on_comment' => Icons.reply_outlined,
        'mention' => Icons.alternate_email_outlined,
        'moderation_result' => Icons.gavel_outlined,
        'system_announcement' => Icons.campaign_outlined,
        'trend_alert' => Icons.trending_up_outlined,
        'viral_post_owner' => Icons.local_fire_department_outlined,
        'weekly_digest' => Icons.summarize_outlined,
        _ => Icons.notifications_none_outlined,
      };
}

class _NotificationSubtitle extends StatelessWidget {
  const _NotificationSubtitle({required this.item});
  final NotificationItem item;

  @override
  Widget build(BuildContext context) {
    if (item.type != 'moderation_result') {
      return Text(item.body, maxLines: 2, overflow: TextOverflow.ellipsis);
    }

    final displayBody = _extractAdminMessage();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (item.ruleViolated != null) ...[
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
            decoration: BoxDecoration(
              color: AppColors.haksiz.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(6),
            ),
            child: Text(
              'İhlal: ${item.ruleViolated}',
              style: const TextStyle(
                fontSize: 11,
                color: AppColors.haksiz,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          const SizedBox(height: 4),
        ],
        Text(displayBody, maxLines: 2, overflow: TextOverflow.ellipsis),
        const SizedBox(height: 6),
        TextButton.icon(
          onPressed: () => context.push('/settings/moderation-history'),
          icon: const Icon(Icons.gavel_outlined, size: 16),
          label: const Text('İtiraz Et'),
          style: TextButton.styleFrom(
            padding: EdgeInsets.zero,
            minimumSize: const Size(44, 32),
            tapTargetSize: MaterialTapTargetSize.shrinkWrap,
            visualDensity: VisualDensity.compact,
          ),
        ),
      ],
    );
  }

  String _extractAdminMessage() {
    if (item.ruleViolated == null) return item.body;
    final prefix = 'Kural ihlali: ${item.ruleViolated}\n\n';
    if (item.body.startsWith(prefix)) return item.body.substring(prefix.length);
    return item.body;
  }
}
