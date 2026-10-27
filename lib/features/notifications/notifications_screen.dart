import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/notifications/notification_deep_link.dart';
import '../../core/settings/preferences_provider.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/utils/count_format.dart';
import '../../core/utils/date_formatter.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/empty_state.dart';
import '../../shared/widgets/error_view.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/karar_chip.dart';
import '../../shared/widgets/karar_sheet.dart';
import '../../shared/widgets/loading_indicator.dart';
import '../profile/widgets/column_app_bar.dart';
import '../profile/widgets/hue_tint.dart';
import 'data/notification_item.dart';
import 'notifications_provider.dart';

class NotificationsScreen extends ConsumerWidget {
  const NotificationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(currentUserProvider);
    final state = ref.watch(notificationsProvider);
    final kc = context.kc;

    return Scaffold(
      // Desktop: title and actions line up with the 680 px list column.
      appBar: ColumnAppBar(
        centerTitle: false,
        title: FittedBox(
          fit: BoxFit.scaleDown,
          alignment: AlignmentDirectional.centerStart,
          child: Text(
            state.unreadCount > 0
                ? 'Bildirimler (${state.unreadCount})'
                : 'Bildirimler',
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: AppTypography.screenTitle.copyWith(color: kc.text1),
          ),
        ),
        actions: [
          if (state.unreadCount > 0)
            TextButton(
              style: TextButton.styleFrom(
                padding: const EdgeInsets.symmetric(horizontal: 10),
                textStyle: AppTypography.labelS,
              ),
              onPressed: () =>
                  ref.read(notificationsProvider.notifier).markAllRead(),
              child: const Text('Tümünü okundu say'),
            ),
          if (state.hasReadItems)
            IconButton(
              icon: const Icon(Icons.cleaning_services_rounded),
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
              icon: Icons.notifications_none_rounded,
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
              padding: EdgeInsets.fromLTRB(
                0,
                4,
                0,
                context.navBottomInset + 24,
              ),
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

    // Start-aligned (the narrower row used to be centred by the parent
    // Column on desktop), with the same 16 px inset as the cards below.
    return Align(
      alignment: AlignmentDirectional.centerStart,
      child: SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 4),
      child: Row(
        children: [
          KararChip(
            icon: Icons.notifications_active_rounded,
            label: prefs.pushEnabled ? 'İzin açık' : 'Bildirim izni ver',
            onTap: () => _requestPermission(context, ref),
          ),
          const SizedBox(width: 8),
          KararChip(
            icon: Icons.volume_up_rounded,
            label: prefs.soundEnabled ? 'Ses açık' : 'Bildirim sesini aç',
            onTap: () => _enableSound(context, ref),
          ),
          const SizedBox(width: 8),
          KararChip(
            icon: Icons.notifications_paused_rounded,
            label: 'Sessize al',
            onTap: () => _showMuteSheet(context, ref),
          ),
          const SizedBox(width: 8),
          KararChip(
            icon: Icons.tune_rounded,
            label: 'Tercihleri yönet',
            onTap: () => context.push('/settings/notifications'),
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
    final duration = await showKararSheet<String>(
      context,
      builder: (ctx) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            KararSheetHeader(
              title: 'Sessize al',
              onClose: () => Navigator.pop(ctx),
            ),
            ListTile(
              leading: const Icon(Icons.schedule_rounded),
              title: const Text('1 saat'),
              onTap: () => Navigator.pop(ctx, '1h'),
            ),
            ListTile(
              leading: const Icon(Icons.today_rounded),
              title: const Text('Bugün'),
              onTap: () => Navigator.pop(ctx, 'today'),
            ),
            ListTile(
              leading: const Icon(Icons.date_range_rounded),
              title: const Text('7 gün'),
              onTap: () => Navigator.pop(ctx, '7d'),
            ),
            ListTile(
              leading: const Icon(Icons.notifications_off_rounded),
              title: const Text('Süresiz'),
              onTap: () => Navigator.pop(ctx, 'indefinite'),
            ),
            const SizedBox(height: 12),
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

    final kc = context.kc;

    if (status == AuthorizationStatus.denied) {
      return Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
        child: Container(
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: kc.haksizSoft,
            borderRadius: KararRadius.all(KararRadius.tile),
          ),
          child: Row(
            children: [
              Icon(Icons.notifications_off_rounded, color: kc.haksizFill),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  'Bildirimler kapalı. Tarayıcı adres çubuğundaki kilit simgesinden bildirimlere izin verebilirsin.',
                  style: AppTypography.bodyS.copyWith(color: kc.text1),
                ),
              ),
            ],
          ),
        ),
      );
    }

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
      child: Container(
        padding: const EdgeInsets.fromLTRB(16, 14, 12, 14),
        decoration: BoxDecoration(
          color: kc.primaryContainer,
          borderRadius: KararRadius.all(KararRadius.surface),
        ),
        child: Row(
          children: [
            const ExcludeSemantics(
              child: Text('🔔', style: TextStyle(fontSize: 24)),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    'Bildirimleri Aç',
                    style: AppTypography.bodyStrong
                        .copyWith(color: kc.onPrimaryContainer),
                  ),
                  Text(
                    'Topluluk karar verince haber ol.',
                    style: AppTypography.bodyS
                        .copyWith(color: kc.onPrimaryContainer),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),
            FilledButton(
              onPressed: _isRequesting ? null : _requestPermission,
              style: FilledButton.styleFrom(
                padding: const EdgeInsets.symmetric(horizontal: 18),
                minimumSize: const Size(64, 44),
                textStyle: AppTypography.label,
              ),
              child: _isRequesting
                  ? SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: kc.onColor,
                      ),
                    )
                  : const Text('Aç'),
            ),
          ],
        ),
      ),
    );
  }
}

class _GuestNotificationsView extends StatelessWidget {
  const _GuestNotificationsView();

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Center(
      child: SingleChildScrollView(
        padding: EdgeInsets.fromLTRB(32, 24, 32, context.navBottomInset + 24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ExcludeSemantics(
              child: Container(
                width: 96,
                height: 96,
                decoration: BoxDecoration(
                  color: kc.primaryContainer,
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  Icons.notifications_rounded,
                  size: 40,
                  color: kc.primary,
                ),
              ),
            ),
            const SizedBox(height: 24),
            Text(
              'Bildirim almak için hesap aç',
              style: AppTypography.headline.copyWith(
                fontSize: 22,
                height: 28 / 22,
                color: kc.text1,
              ),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 12),
            Text(
              'Postun oylanınca veya topluluk karar verince seni haberdar edelim.',
              style: AppTypography.body.copyWith(color: kc.text2),
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 32),
            ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 360),
              child: Column(
                children: [
                  KararButton(
                    label: 'Hesap Aç',
                    size: KararButtonSize.large,
                    onPressed: () => context.push('/auth/register'),
                  ),
                  const SizedBox(height: 12),
                  KararButton(
                    label: 'Giriş Yap',
                    variant: KararButtonVariant.outlined,
                    size: KararButtonSize.large,
                    onPressed: () => context.push('/auth/login'),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// "Dün Kaçırdıkların" digest: an ink card in the style of the weekly hero
/// (radial violet-ink gradient, white text, gold vote counts).
class _DigestWidget extends ConsumerWidget {
  const _DigestWidget();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(digestPostsProvider);
    final kc = context.kc;

    return async.when(
      loading: () => const SizedBox.shrink(),
      error: (_, __) => const SizedBox.shrink(),
      data: (posts) {
        if (posts.isEmpty) return const SizedBox.shrink();
        return Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
          child: Container(
            decoration: BoxDecoration(
              gradient: KararGradients.hero,
              borderRadius: KararRadius.all(KararRadius.surface),
              border: context.isDark
                  ? Border.all(color: KararShadow.cardHairlineDark)
                  : null,
              boxShadow: KararShadow.soft(Theme.of(context).brightness),
            ),
            padding: const EdgeInsets.fromLTRB(18, 16, 10, 6),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsets.only(right: 8),
                  child: Row(
                    children: [
                      // Rounded Material icon instead of the colour emoji
                      // (matches the rest of the icon set).
                      ExcludeSemantics(
                        child: Icon(
                          Icons.insights_rounded,
                          size: 20,
                          color: kc.gold,
                        ),
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          'Dün Kaçırdıkların',
                          style: AppTypography.bodyStrong.copyWith(
                            fontSize: 16,
                            fontWeight: FontWeight.w800,
                            color: kc.onColor,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 8),
                ...posts.map((p) => _DigestItem(post: p)),
                Align(
                  alignment: AlignmentDirectional.centerEnd,
                  child: TextButton(
                    style: TextButton.styleFrom(
                      foregroundColor: kc.onColor,
                      padding: const EdgeInsets.symmetric(horizontal: 10),
                      minimumSize: const Size(44, 44),
                      textStyle: AppTypography.label,
                    ),
                    onPressed: () => context.go('/'),
                    child: Text(
                      'Hepsini Gör →',
                      style: AppTypography.label.copyWith(color: kc.onColor),
                    ),
                  ),
                ),
              ],
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
    final kc = context.kc;
    final votesLabel = '${formatCompactCount(post.totalVotes)} oy';
    return InkWell(
      onTap: () => context.push('/posts/${post.id}', extra: post),
      borderRadius: KararRadius.all(KararRadius.sm),
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 36),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(0, 4, 8, 4),
          child: Row(
            children: [
              ExcludeSemantics(
                child: Text(
                  '•  ',
                  style: TextStyle(
                    color: kc.onColor,
                    fontWeight: FontWeight.w800,
                  ),
                ),
              ),
              Expanded(
                child: Text(
                  post.title,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.bodyS.copyWith(color: kc.onColor),
                ),
              ),
              const SizedBox(width: 8),
              Text(
                '→ $votesLabel',
                style: AppTypography.labelS.copyWith(
                  fontWeight: FontWeight.w800,
                  color: kc.gold,
                  fontFeatures: AppTypography.tabular,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Icon tile colours per notification type (spec §6.6).
({Color bg, Color fg}) _tileColors(BuildContext context, String type) {
  final kc = context.kc;
  final dark = context.isDark;

  // Dark: a lighter tone of the same hue (not a lerp to white, which made
  // amber a muddy beige). See [hueTint].
  ({Color bg, Color fg}) tinted(Color hue) => hueTint(kc, hue);

  return switch (type) {
    'verdict_milestone' => (bg: kc.warnSoft, fg: kc.goldText),
    'comment_on_post' ||
    'verdict_reminder' =>
      (bg: kc.primaryContainer, fg: dark ? kc.navActive : kc.primary),
    'reply_on_comment' => tinted(CategoryStyle.ocean.start),
    'mention' => tinted(CategoryStyle.magenta.start),
    'moderation_result' => (bg: kc.haksizSoft, fg: kc.haksizFill),
    'trend_alert' || 'viral_post_owner' => tinted(CategoryStyle.amber.start),
    'weekly_digest' => (bg: kc.hakliSoft, fg: dark ? kc.hakliText : kc.hakliFill),
    _ => (bg: kc.muted, fg: kc.text2),
  };
}

class _NotificationTile extends ConsumerWidget {
  const _NotificationTile({required this.item});
  final NotificationItem item;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final kc = context.kc;
    final notifier = ref.read(notificationsProvider.notifier);
    final colors = _tileColors(context, item.type);
    final radius = KararRadius.all(KararRadius.tile);

    // Same 16 px inset as the digest card and the chips row, so every
    // rounded surface in the column shares its edges.
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 2),
      child: Dismissible(
        key: ValueKey(item.id),
        direction: DismissDirection.endToStart,
        background: Container(
          alignment: AlignmentDirectional.centerEnd,
          padding: const EdgeInsetsDirectional.only(end: 20),
          decoration: BoxDecoration(
            color: kc.haksizSoft,
            borderRadius: radius,
          ),
          child: Icon(Icons.delete_outline_rounded, color: kc.haksizText),
        ),
        onDismissed: (_) {
          ref.read(analyticsServiceProvider).logNotificationDismissed(
                notificationId: item.id,
                type: item.type,
              );
          notifier.dismiss(item.id);
        },
        child: Material(
          color: item.isRead
              ? Colors.transparent
              : kc.primaryContainer.withValues(alpha: 0.45),
          borderRadius: radius,
          child: InkWell(
            borderRadius: radius,
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
            child: ConstrainedBox(
              constraints: const BoxConstraints(minHeight: 72),
              child: Padding(
                padding: const EdgeInsets.fromLTRB(12, 12, 12, 12),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    ExcludeSemantics(
                      child: Container(
                        width: 44,
                        height: 44,
                        decoration: BoxDecoration(
                          color: colors.bg,
                          borderRadius: KararRadius.all(KararRadius.iconTile),
                        ),
                        child: Icon(
                          _iconForType(item.type),
                          size: 22,
                          color: colors.fg,
                        ),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            item.title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: AppTypography.bodyStrong.copyWith(
                              fontWeight: item.isRead
                                  ? FontWeight.w600
                                  : FontWeight.w800,
                              color: kc.text1,
                            ),
                          ),
                          const SizedBox(height: 2),
                          _NotificationSubtitle(item: item),
                        ],
                      ),
                    ),
                    const SizedBox(width: 8),
                    Column(
                      crossAxisAlignment: CrossAxisAlignment.end,
                      children: [
                        Text(
                          DateFormatter.relative(item.createdAt),
                          style: AppTypography.meta.copyWith(color: kc.text3),
                        ),
                        if (!item.isRead) ...[
                          const SizedBox(height: 8),
                          Container(
                            width: 8,
                            height: 8,
                            decoration: BoxDecoration(
                              color: kc.primary,
                              shape: BoxShape.circle,
                            ),
                          ),
                        ],
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
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
        'verdict_milestone' => Icons.emoji_events_rounded,
        'verdict_reminder' => Icons.how_to_vote_rounded,
        'comment_on_post' => Icons.chat_bubble_rounded,
        'reply_on_comment' => Icons.reply_rounded,
        'mention' => Icons.alternate_email_rounded,
        'moderation_result' => Icons.gavel_rounded,
        'system_announcement' => Icons.campaign_rounded,
        'trend_alert' => Icons.trending_up_rounded,
        'viral_post_owner' => Icons.local_fire_department_rounded,
        'weekly_digest' => Icons.summarize_rounded,
        _ => Icons.notifications_rounded,
      };
}

class _NotificationSubtitle extends StatelessWidget {
  const _NotificationSubtitle({required this.item});
  final NotificationItem item;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final bodyStyle = AppTypography.bodyS.copyWith(color: kc.text2);
    if (item.type != 'moderation_result') {
      return Text(
        item.body,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
        style: bodyStyle,
      );
    }

    final displayBody = _extractAdminMessage();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (item.ruleViolated != null) ...[
          const SizedBox(height: 2),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
            decoration: BoxDecoration(
              color: kc.haksizSoft,
              borderRadius: KararRadius.all(KararRadius.xs),
            ),
            child: Text(
              'İhlal: ${item.ruleViolated}',
              style: AppTypography.micro.copyWith(
                color: kc.haksizText,
                letterSpacing: 0,
              ),
            ),
          ),
          const SizedBox(height: 4),
        ],
        Text(
          displayBody,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: bodyStyle,
        ),
        const SizedBox(height: 6),
        FilledButton.tonalIcon(
          onPressed: () => context.push('/settings/moderation-history'),
          icon: const Icon(Icons.gavel_rounded, size: 16),
          label: const Text('İtiraz Et'),
          style: FilledButton.styleFrom(
            backgroundColor: kc.primaryContainer,
            foregroundColor: kc.onPrimaryContainer,
            minimumSize: const Size(44, 36),
            padding: const EdgeInsets.symmetric(horizontal: 14),
            textStyle: AppTypography.labelS,
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
