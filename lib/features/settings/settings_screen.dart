import 'package:flutter/material.dart';
import 'package:flutter_cache_manager/flutter_cache_manager.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:in_app_review/in_app_review.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/ads/ad_service.dart';
import '../../core/api/api_exception.dart';
import '../../core/auth/auth_service.dart';
import '../../core/providers.dart';
import '../../core/settings/preferences_provider.dart';
import '../../core/theme/font_size_provider.dart';
import '../../core/theme/theme_provider.dart';
import '../../core/history/history_provider.dart';
import '../../core/layout/breakpoints.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';
import '../feed/categories_provider.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_sheet.dart';
import '../profile/widgets/column_app_bar.dart';
import 'widgets/settings_group.dart';

enum SettingsInitialSection { top, notifications }

class SettingsScreen extends ConsumerStatefulWidget {
  const SettingsScreen({
    super.key,
    this.initialSection = SettingsInitialSection.top,
  });

  final SettingsInitialSection initialSection;

  @override
  ConsumerState<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends ConsumerState<SettingsScreen> {
  final _notificationsSectionKey = GlobalKey();

  @override
  void initState() {
    super.initState();
    if (widget.initialSection == SettingsInitialSection.notifications) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        final context = _notificationsSectionKey.currentContext;
        if (context == null) return;
        Scrollable.ensureVisible(
          context,
          duration: const Duration(milliseconds: 350),
          curve: Curves.easeOutCubic,
          alignment: 0.02,
        );
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final user = ref.watch(currentUserProvider);
    final themeMode = ref.watch(themeProvider);
    final fontSize = ref.watch(fontSizeProvider);
    final prefs = ref.watch(userPreferencesProvider);
    final kc = context.kc;

    return Scaffold(
      // Desktop: back button and title line up with the 680 px column.
      appBar: const ColumnAppBar(
        title: Text('Ayarlar'),
        centerTitle: true,
      ),
      body: CenteredContent(
        child: ListView(
          padding: EdgeInsets.only(bottom: context.navBottomInset + 24),
          children: [
            // ── Görünüm ─────────────────────────────────────────────────────────
            const _SectionHeader('Görünüm'),
            SettingsGroup(
              children: [
                _SettingsTile(
                  icon: Icons.contrast_rounded,
                  title: 'Tema',
                  subtitle: _themeName(themeMode),
                  onTap: () => _showThemePicker(context, ref, themeMode),
                ),
                _SettingsTile(
                  icon: Icons.format_size_rounded,
                  title: 'Yazı Boyutu',
                  subtitle: fontSize.label,
                  onTap: () => _showFontSizePicker(context, ref, fontSize),
                ),
              ],
            ),

            // ── Bildirimler ──────────────────────────────────────────────────────
            KeyedSubtree(
              key: _notificationsSectionKey,
              child: const _SectionHeader('Bildirimler'),
            ),
            FutureBuilder<bool>(
              future: ref.read(notificationServiceProvider).isDenied(),
              builder: (context, snapshot) {
                if (snapshot.data == true) {
                  final notifications = ref.read(notificationServiceProvider);
                  return Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: SettingsGroup(
                      children: [
                        _SettingsTile(
                          icon: Icons.notifications_off_rounded,
                          title: 'Bildirimler kapalı',
                          subtitle: notifications.deniedPermissionHelpText,
                          destructive: true,
                          trailing:
                              notifications.canOpenPlatformNotificationSettings
                                  ? TextButton(
                                      onPressed: () =>
                                          notifications.openSettings(),
                                      child: const Text('Ayarlara Git'),
                                    )
                                  : null,
                          onTap:
                              notifications.canOpenPlatformNotificationSettings
                                  ? () => notifications.openSettings()
                                  : null,
                        ),
                      ],
                    ),
                  );
                }
                return const SizedBox.shrink();
              },
            ),
            SettingsGroup(
              children: [
                _SettingsTile(
                  icon: Icons.notifications_rounded,
                  title: 'Push bildirimleri',
                  subtitle: 'Tüm push bildirimlerini aç/kapat',
                  trailing: Switch(
                    value: prefs.pushEnabled,
                    onChanged: (v) => _setPushEnabled(context, ref, v),
                  ),
                ),
                _SettingsTile(
                  icon: Icons.volume_up_rounded,
                  title: 'Bildirim sesi',
                  subtitle: 'Ses ve kanal ayarını cihazdan yönet',
                  trailing: Switch(
                    value: prefs.soundEnabled,
                    onChanged: prefs.pushEnabled
                        ? (v) => ref
                            .read(userPreferencesProvider.notifier)
                            .update((s) => s.copyWith(soundEnabled: v))
                        : null,
                  ),
                  onTap: () =>
                      ref.read(notificationServiceProvider).openSettings(),
                ),
                _SettingsTile(
                  icon: Icons.emoji_events_rounded,
                  title: 'Topluluk kararı',
                  subtitle: 'Postun oy milestonelerine ulaşınca',
                  trailing: Switch(
                    value: prefs.verdictMilestone,
                    onChanged: prefs.pushEnabled
                        ? (v) => ref
                            .read(userPreferencesProvider.notifier)
                            .update((s) => s.copyWith(verdictMilestone: v))
                        : null,
                  ),
                ),
                _SettingsTile(
                  icon: Icons.chat_bubble_rounded,
                  title: 'Yeni yorum',
                  subtitle: 'Postuna yeni yorum gelince',
                  trailing: Switch(
                    value: prefs.newComment,
                    onChanged: prefs.pushEnabled
                        ? (v) => ref
                            .read(userPreferencesProvider.notifier)
                            .update((s) => s.copyWith(newComment: v))
                        : null,
                  ),
                ),
                _SettingsTile(
                  icon: Icons.reply_rounded,
                  title: 'Yorum yanıtı',
                  subtitle: 'Yorumuna yanıt gelince',
                  trailing: Switch(
                    value: prefs.commentReply,
                    onChanged: prefs.pushEnabled
                        ? (v) => ref
                            .read(userPreferencesProvider.notifier)
                            .update((s) => s.copyWith(commentReply: v))
                        : null,
                  ),
                ),
                _SettingsTile(
                  icon: Icons.alternate_email_rounded,
                  title: 'Bahsedilme',
                  subtitle: 'Yorumunda @etiketlenince',
                  trailing: Switch(
                    value: prefs.notifyOnMention,
                    onChanged: prefs.pushEnabled
                        ? (v) => ref
                            .read(userPreferencesProvider.notifier)
                            .update((s) => s.copyWith(notifyOnMention: v))
                        : null,
                  ),
                ),
                _SettingsTile(
                  icon: Icons.verified_user_rounded,
                  title: 'Moderasyon',
                  subtitle: 'Post onaylandı/kaldırıldı',
                  trailing: Switch(
                    value: prefs.postModeration,
                    onChanged: prefs.pushEnabled
                        ? (v) => ref
                            .read(userPreferencesProvider.notifier)
                            .update((s) => s.copyWith(postModeration: v))
                        : null,
                  ),
                ),
                _SettingsTile(
                  icon: Icons.trending_up_rounded,
                  title: 'Viral uyarısı',
                  subtitle: 'Postun öne çıkınca',
                  trailing: Switch(
                    value: prefs.notifyOnTrend,
                    onChanged: prefs.pushEnabled
                        ? (v) => ref
                            .read(userPreferencesProvider.notifier)
                            .update((s) => s.copyWith(notifyOnTrend: v))
                        : null,
                  ),
                ),
                _SettingsTile(
                  icon: Icons.summarize_rounded,
                  title: 'Haftalık özet',
                  subtitle: 'Kaçırdığın içerikler her hafta',
                  trailing: Switch(
                    value: prefs.notifyOnDigest,
                    onChanged: prefs.pushEnabled
                        ? (v) => ref
                            .read(userPreferencesProvider.notifier)
                            .update((s) => s.copyWith(notifyOnDigest: v))
                        : null,
                  ),
                ),
                _SettingsTile(
                  icon: Icons.notifications_paused_rounded,
                  title: 'Sessiz saatler',
                  subtitle: prefs.silentHoursEnabled
                      ? '${prefs.silentStart} – ${prefs.silentEnd}'
                      : 'Kapalı',
                  trailing: Switch(
                    value: prefs.silentHoursEnabled,
                    onChanged: (v) => ref
                        .read(userPreferencesProvider.notifier)
                        .update((s) => s.copyWith(silentHoursEnabled: v)),
                  ),
                  onTap: () => _showSilentHoursPicker(context, ref, prefs),
                ),
              ],
            ),
            const SizedBox(height: 12),
            SettingsGroup(
              children: [
                _SettingsTile(
                  icon: Icons.history_rounded,
                  title: 'Arama geçmişi',
                  subtitle: 'Son aramalarını temizle',
                  onTap: () => _clearSearchHistory(context, ref),
                ),
                Consumer(
                  builder: (context, ref, _) {
                    final viewedCount = ref.watch(historyProvider).length;
                    return _SettingsTile(
                      icon: Icons.visibility_rounded,
                      title: 'Görüntüleme geçmişi',
                      subtitle: viewedCount > 0
                          ? 'Görüntülediğin $viewedCount gönderi soluk gösteriliyor.'
                          : 'Geçmiş boş.',
                      onTap: viewedCount > 0
                          ? () => _clearViewHistory(context, ref)
                          : null,
                    );
                  },
                ),
                _SettingsTile(
                  icon: Icons.category_rounded,
                  title: 'Takip edilen kategoriler',
                  subtitle: 'Kategori takibini sıfırla',
                  onTap: () => _clearFollowedCategories(context, ref),
                ),
                _SettingsTile(
                  icon: Icons.email_rounded,
                  title: 'E-posta bildirimleri',
                  subtitle: 'Haftalık özet e-postası',
                  trailing: Switch(
                    value: prefs.emailNewsletter,
                    onChanged: (v) => ref
                        .read(userPreferencesProvider.notifier)
                        .update((s) => s.copyWith(emailNewsletter: v)),
                  ),
                ),
              ],
            ),

            // ── Gizlilik ────────────────────────────────────────────────────────
            const _SectionHeader('Gizlilik'),
            SettingsGroup(
              children: [
                _SettingsTile(
                  icon: Icons.shield_rounded,
                  title: 'Gizlilik Özeti',
                  subtitle: 'Ne topluyoruz, neden ve ne kadar süre?',
                  onTap: () => context.push('/legal/privacy-summary'),
                ),
                _SettingsTile(
                  icon: Icons.how_to_vote_rounded,
                  title: 'Oy gizliliği',
                  subtitle: 'Oy verdiğim postları profilimde göster',
                  trailing: Switch(
                    value: prefs.showVotesOnProfile,
                    onChanged: (v) => ref
                        .read(userPreferencesProvider.notifier)
                        .update((s) => s.copyWith(showVotesOnProfile: v)),
                  ),
                ),
                _SettingsTile(
                  icon: Icons.visibility_rounded,
                  title: 'Profil görünürlüğü',
                  subtitle: 'Karma\'mı herkese göster',
                  trailing: Switch(
                    value: prefs.showKarmaToOthers,
                    onChanged: (v) => ref
                        .read(userPreferencesProvider.notifier)
                        .update((s) => s.copyWith(showKarmaToOthers: v)),
                  ),
                ),
                _SettingsTile(
                  icon: Icons.analytics_rounded,
                  title: 'Analitik',
                  subtitle: 'Uygulama kullanım verilerini anonim olarak paylaş',
                  trailing: Switch(
                    value: prefs.analyticsEnabled,
                    onChanged: (v) => ref
                        .read(userPreferencesProvider.notifier)
                        .update((s) => s.copyWith(analyticsEnabled: v)),
                  ),
                ),
                _SettingsTile(
                  icon: Icons.ads_click_rounded,
                  title: 'Reklam Tercihleri',
                  subtitle: 'Kişiselleştirilmiş reklam ayarlarını yönet',
                  onTap: () => AdService.instance.showPrivacyOptionsForm(),
                ),
                _SettingsTile(
                  icon: Icons.download_rounded,
                  title: 'Verilerimi indir',
                  subtitle: 'Hesap verilerini e-postayla al (KVKK)',
                  onTap: () => _requestDataExport(context, ref),
                ),
                if (user != null)
                  _SettingsTile(
                    icon: Icons.block_rounded,
                    title: 'Engellenen kullanıcılar',
                    subtitle: 'Engellediğin hesapları yönet',
                    onTap: () => context.push('/settings/blocked-users'),
                  ),
              ],
            ),

            // ── Hesap ───────────────────────────────────────────────────────────
            if (user != null) ...[
              const _SectionHeader('Hesap'),
              Consumer(
                builder: (context, ref, _) {
                  final pendingEmail = ref.watch(pendingEmailChangeProvider);
                  if (pendingEmail == null) return const SizedBox.shrink();
                  return _PendingEmailBanner(
                    pendingEmail: pendingEmail,
                    onVerify: () => context.push('/settings/change-email'),
                    onCancel: () async {
                      await ref
                          .read(authServiceProvider)
                          .clearPendingEmailChange();
                      ref.read(pendingEmailChangeProvider.notifier).state =
                          null;
                    },
                  );
                },
              ),
              SettingsGroup(
                children: [
                  if (user.authProvider != 'google') ...[
                    _SettingsTile(
                      icon: Icons.lock_rounded,
                      title: 'Şifre değiştir',
                      onTap: () => context.push('/settings/change-password'),
                    ),
                    _SettingsTile(
                      icon: Icons.email_rounded,
                      title: 'E-posta değiştir',
                      onTap: () => context.push('/settings/change-email'),
                    ),
                  ],
                  _SettingsTile(
                    icon: Icons.shield_rounded,
                    title: 'İki faktörlü doğrulama',
                    subtitle: user.is2faEnabled
                        ? 'Aktif'
                        : 'Hesabını ekstra güvence altına al',
                    onTap: user.is2faEnabled
                        ? () => _disable2fa(context, ref)
                        : () => context.push('/settings/2fa-setup'),
                  ),
                  _SettingsTile(
                    icon: Icons.devices_rounded,
                    title: 'Aktif oturumlar',
                    subtitle: 'Giriş yaptığın cihazları yönet',
                    onTap: () => context.push('/settings/sessions'),
                  ),
                ],
              ),
            ],

            // ── Yasal ───────────────────────────────────────────────────────────
            const _SectionHeader('Yasal'),
            SettingsGroup(
              children: [
                _SettingsTile(
                  icon: Icons.description_rounded,
                  title: 'Kullanım Koşulları',
                  onTap: () => context.push('/legal/terms'),
                ),
                _SettingsTile(
                  icon: Icons.privacy_tip_rounded,
                  title: 'Gizlilik Politikası',
                  onTap: () => context.push('/legal/privacy'),
                ),
                _SettingsTile(
                  icon: Icons.gavel_rounded,
                  title: 'Topluluk Kuralları',
                  onTap: () => context.push('/legal/community'),
                ),
                _SettingsTile(
                  icon: Icons.policy_rounded,
                  title: 'İçerik Politikası',
                  subtitle: 'Politika değişiklikleri ve geçmişi',
                  onTap: () => context.push('/legal/content-policy'),
                ),
                _SettingsTile(
                  icon: Icons.shield_rounded,
                  title: 'Moderasyon Şeffaflığı',
                  subtitle: 'Kaldırma oranları ve topluluk güveni raporu',
                  onTap: () => context.push('/legal/moderation-transparency'),
                ),
                _SettingsTile(
                  icon: Icons.contact_mail_rounded,
                  title: 'İletişim ve Yer Sağlayıcı Bilgileri',
                  subtitle: '5651 bildirim adresleri ve destek',
                  onTap: () => context.push('/legal/contact'),
                ),
                _SettingsTile(
                  icon: Icons.copyright_rounded,
                  title: 'Telif / FSEK Şikayeti',
                  subtitle: '72 saat yanıt hedefli kaldırma formu',
                  onTap: () => context.push('/legal/copyright'),
                ),
                if (user != null)
                  _SettingsTile(
                    icon: Icons.shield_moon_rounded,
                    title: 'Moderasyon Geçmişim',
                    subtitle: 'Kaldırılan içerikler, uyarılar ve itirazlar',
                    onTap: () => context.push('/settings/moderation-history'),
                  ),
              ],
            ),

            // ── Destek ──────────────────────────────────────────────────────────
            const _SectionHeader('Destek'),
            SettingsGroup(
              children: [
                const _SettingsTile(
                  icon: Icons.star_rounded,
                  title: 'Uygulamayı değerlendir',
                  onTap: _rateApp,
                ),
                _SettingsTile(
                  icon: Icons.help_rounded,
                  title: 'Sık Sorulan Sorular',
                  subtitle: 'Merak ettiklerini buradan öğren',
                  onTap: () => context.push('/legal/faq'),
                ),
                _SettingsTile(
                  icon: Icons.bug_report_rounded,
                  title: 'Hata bildir',
                  onTap: () => context.push('/settings/feedback'),
                ),
                _SettingsTile(
                  icon: Icons.mail_rounded,
                  title: 'İletişim',
                  subtitle: 'destek@karar.app',
                  onTap: () async {
                    final uri = Uri(scheme: 'mailto', path: 'destek@karar.app');
                    if (await canLaunchUrl(uri)) await launchUrl(uri);
                  },
                ),
                _SettingsTile(
                  icon: Icons.language_rounded,
                  title: 'Dil',
                  subtitle: 'Türkçe',
                  onTap: () => _showLanguagePicker(context),
                ),
                _SettingsTile(
                  icon: Icons.cleaning_services_rounded,
                  title: 'Önbelleği temizle',
                  subtitle: 'Yerel görselleri ve geçici verileri sil',
                  onTap: () => _clearCache(context),
                ),
              ],
            ),

            // ── Hesabı kapat / sil (danger group, last) ────────────────────────
            if (user != null) ...[
              const SizedBox(height: KararSpace.sectionGap),
              SettingsGroup(
                children: [
                  _SettingsTile(
                    icon: Icons.child_care_rounded,
                    title: '18 yaş altıyım',
                    subtitle: 'Hesabımı 18 yaş kuralı nedeniyle kapat',
                    destructive: true,
                    onTap: () => _confirmUnderageLogout(context, ref, user),
                  ),
                  _SettingsTile(
                    icon: Icons.delete_forever_rounded,
                    title: 'Hesabı sil',
                    destructive: true,
                    onTap: () => _confirmDeleteAccount(context, ref, user),
                  ),
                ],
              ),
            ],
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 24),
              child: Text(
                'Karar v1.0.0 (build 45)',
                textAlign: TextAlign.center,
                style: AppTypography.meta.copyWith(color: kc.text3),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _clearCache(BuildContext context) async {
    await DefaultCacheManager().emptyCache();
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Önbellek temizlendi.')),
      );
    }
  }

  Future<void> _clearViewHistory(BuildContext context, WidgetRef ref) async {
    await ref.read(historyProvider.notifier).clear();
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Görüntüleme geçmişi temizlendi.')),
      );
    }
  }

  Future<void> _clearSearchHistory(BuildContext context, WidgetRef ref) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove('search_history');
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Arama geçmişi temizlendi.')),
      );
    }
  }

  Future<void> _clearFollowedCategories(
      BuildContext context, WidgetRef ref) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove('followed_categories');
    ref.invalidate(followedCategoriesProvider);
    if (context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Kategori takibi sıfırlandı.')),
      );
    }
  }

  void _showFontSizePicker(
      BuildContext context, WidgetRef ref, AppFontSize current) {
    showKararSheet<void>(
      context,
      builder: (ctx) => SafeArea(
        child: RadioGroup<AppFontSize>(
          groupValue: current,
          onChanged: (v) {
            if (v != null) ref.read(fontSizeProvider.notifier).setFontSize(v);
            Navigator.pop(ctx);
          },
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              KararSheetHeader(
                title: 'Yazı Boyutu Seç',
                onClose: () => Navigator.pop(ctx),
              ),
              for (final fs in AppFontSize.values)
                RadioListTile<AppFontSize>(
                  title: Text(fs.label),
                  value: fs,
                ),
              const SizedBox(height: 12),
            ],
          ),
        ),
      ),
    );
  }

  void _showThemePicker(
      BuildContext context, WidgetRef ref, ThemeMode current) {
    showKararSheet<void>(
      context,
      builder: (ctx) => SafeArea(
        child: RadioGroup<ThemeMode>(
          groupValue: current,
          onChanged: (v) {
            if (v != null) ref.read(themeProvider.notifier).setMode(v);
            Navigator.pop(ctx);
          },
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              KararSheetHeader(
                title: 'Tema Seç',
                onClose: () => Navigator.pop(ctx),
              ),
              for (final mode in ThemeMode.values)
                RadioListTile<ThemeMode>(
                  title: Text(_themeName(mode)),
                  secondary: Icon(_themeIcon(mode)),
                  value: mode,
                ),
              const SizedBox(height: 12),
            ],
          ),
        ),
      ),
    );
  }

  static String _themeName(ThemeMode mode) => switch (mode) {
        ThemeMode.light => 'Açık',
        ThemeMode.dark => 'Koyu',
        ThemeMode.system => 'Sistem',
      };

  static IconData _themeIcon(ThemeMode mode) => switch (mode) {
        ThemeMode.light => Icons.light_mode_rounded,
        ThemeMode.dark => Icons.dark_mode_rounded,
        ThemeMode.system => Icons.brightness_auto_rounded,
      };

  Future<void> _setPushEnabled(
    BuildContext context,
    WidgetRef ref,
    bool enabled,
  ) async {
    if (!enabled) {
      await _showReduceOrDisableSheet(context, ref);
      return;
    }

    final preferences = ref.read(userPreferencesProvider.notifier);
    final notifications = ref.read(notificationServiceProvider);
    await preferences.update((s) => s.copyWith(pushEnabled: enabled));
    await notifications.maybeRequestPermission(force: true);
    final denied = await notifications.isDenied();
    if (!context.mounted || !denied) return;

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: const Text(
          'Bildirimler kapalı. İstersen cihaz ayarlarından tekrar açabilirsin.',
        ),
        action: SnackBarAction(
          label: 'Ayarlar',
          onPressed: () => notifications.openSettings(),
        ),
      ),
    );
  }

  Future<void> _showReduceOrDisableSheet(
      BuildContext context, WidgetRef ref) async {
    final result = await showKararSheet<_PushAction>(
      context,
      builder: (ctx) {
        final kc = ctx.kc;
        return SafeArea(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              KararSheetHeader(
                title: 'Bildirimleri azalt mı, yoksa tamamen kapat mı?',
                onClose: () => Navigator.pop(ctx, _PushAction.cancel),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 0, 20, 12),
                child: Text(
                  'Bildirimleri tamamen kapatırsan yorum, yanıt ve önemli güncellemeleri kaçırabilirsin.',
                  style: AppTypography.bodyS.copyWith(color: kc.text2),
                ),
              ),
              ListTile(
                leading: Icon(Icons.notifications_paused_rounded,
                    color: kc.primary),
                title: const Text('Daha az bildirim al'),
                subtitle: const Text(
                    'Sadece yorumlar ve bahsedilmeler — trend ve özet bildirimleri kapanır'),
                onTap: () => Navigator.pop(ctx, _PushAction.reduce),
              ),
              ListTile(
                leading:
                    Icon(Icons.notifications_off_rounded,
                        color: kc.voteIcon(false)),
                title: Text('Bildirimleri tamamen kapat',
                    style: TextStyle(color: kc.haksizText)),
                subtitle: const Text('Hiçbir push bildirimi almayacaksın'),
                onTap: () => Navigator.pop(ctx, _PushAction.disable),
              ),
              ListTile(
                leading: const Icon(Icons.close_rounded),
                title: const Text('Vazgeç'),
                onTap: () => Navigator.pop(ctx, _PushAction.cancel),
              ),
              const SizedBox(height: 8),
            ],
          ),
        );
      },
    );

    if (!context.mounted) return;
    final notifications = ref.read(notificationServiceProvider);

    switch (result) {
      case _PushAction.reduce:
        await ref.read(userPreferencesProvider.notifier).update(
              (s) => s.copyWith(
                notifyOnTrend: false,
                notifyOnDigest: false,
                verdictMilestone: false,
              ),
            );
        if (context.mounted) {
          ScaffoldMessenger.of(context).showSnackBar(
            const SnackBar(
              content: Text(
                  'Bildirimler azaltıldı. Sadece yorum ve bahsedilmeler gelecek.'),
            ),
          );
        }
      case _PushAction.disable:
        await ref
            .read(userPreferencesProvider.notifier)
            .update((s) => s.copyWith(pushEnabled: false));
        await notifications.deleteCurrentToken();
      case _PushAction.cancel || null:
        break;
    }
  }

  static Future<void> _rateApp() async {
    final review = InAppReview.instance;
    if (await review.isAvailable()) {
      await review.requestReview();
    } else {
      await review.openStoreListing();
    }
  }

  void _showLanguagePicker(BuildContext context) {
    showKararSheet<void>(
      context,
      builder: (ctx) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            KararSheetHeader(
              title: 'Dil Seç',
              onClose: () => Navigator.pop(ctx),
            ),
            ListTile(
              title: const Text('Türkçe'),
              trailing: Icon(Icons.check_rounded, color: ctx.kc.primary),
              onTap: () => Navigator.pop(ctx),
            ),
            ListTile(
              title: const Text('English'),
              subtitle: const Text('Yakında / Coming Soon'),
              enabled: false,
              trailing: const Icon(Icons.lock_clock_rounded, size: 18),
              onTap: () {},
            ),
            const SizedBox(height: 12),
          ],
        ),
      ),
    );
  }

  void _disable2fa(BuildContext context, WidgetRef ref) {
    final passwordCtrl = TextEditingController();
    var isDisabling = false;
    String? errorText;

    showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setState) {
          Future<void> submit() async {
            if (passwordCtrl.text.trim().isEmpty) {
              setState(() => errorText = 'Şifreni gir.');
              return;
            }
            setState(() {
              isDisabling = true;
              errorText = null;
            });
            try {
              await ref.read(authServiceProvider).disable2fa();
              if (!ctx.mounted) return;
              Navigator.pop(ctx);
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(
                    content: Text('İki faktörlü doğrulama kapatıldı.')),
              );
            } on ApiException catch (e) {
              if (!ctx.mounted) return;
              setState(() {
                isDisabling = false;
                errorText = e.friendlyMessage;
              });
            } catch (_) {
              if (!ctx.mounted) return;
              setState(() {
                isDisabling = false;
                errorText = 'Bir hata oluştu. Tekrar dene.';
              });
            }
          }

          return AlertDialog(
            title: const Text('2FA Devre Dışı Bırak?'),
            content: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                    'Hesabınızın güvenliği azalacaktır. Devam etmek için şifreni gir.'),
                const SizedBox(height: 16),
                TextField(
                  controller: passwordCtrl,
                  enabled: !isDisabling,
                  obscureText: true,
                  autofocus: true,
                  decoration:
                      InputDecoration(labelText: 'Şifre', errorText: errorText),
                ),
              ],
            ),
            actions: [
              TextButton(
                  onPressed: isDisabling ? null : () => Navigator.pop(ctx),
                  child: const Text('Vazgeç')),
              FilledButton(
                onPressed: isDisabling ? null : submit,
                child: isDisabling
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2))
                    : const Text('Kapat'),
              ),
            ],
          );
        },
      ),
    ).whenComplete(passwordCtrl.dispose);
  }

  void _showSilentHoursPicker(
      BuildContext context, WidgetRef ref, UserPreferences prefs) async {
    final start = await showTimePicker(
      context: context,
      initialTime: _parseTime(prefs.silentStart),
      helpText: 'Başlangıç Saati',
    );
    if (start == null || !context.mounted) return;

    final end = await showTimePicker(
      context: context,
      initialTime: _parseTime(prefs.silentEnd),
      helpText: 'Bitiş Saati',
    );
    if (end == null || !context.mounted) return;

    ref.read(userPreferencesProvider.notifier).update((s) => s.copyWith(
          silentStart: _formatTime(start),
          silentEnd: _formatTime(end),
          silentHoursEnabled: true,
        ));
  }

  Future<void> _requestDataExport(BuildContext context, WidgetRef ref) async {
    if (ref.read(currentUserProvider) == null) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Veri indirmek için giriş yapmalısın.')),
      );
      return;
    }
    try {
      await ref.read(authServiceProvider).requestDataExport();
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
              content: Text(
                  'Veri paketini hazırlıyoruz. Hazır olunca e-posta göndereceğiz.')),
        );
      }
    } catch (_) {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Bir hata oluştu. Tekrar dene.')),
        );
      }
    }
  }

  TimeOfDay _parseTime(String time) {
    final parts = time.split(':');
    return TimeOfDay(hour: int.parse(parts[0]), minute: int.parse(parts[1]));
  }

  String _formatTime(TimeOfDay time) {
    final h = time.hour.toString().padLeft(2, '0');
    final m = time.minute.toString().padLeft(2, '0');
    return '$h:$m';
  }

  void _confirmUnderageLogout(
      BuildContext context, WidgetRef ref, AuthUser user) {
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Hesabı Kapat?'),
        content: const Text(
          'Karar topluluğu 18 yaş ve üzeri bireyler içindir. 18 yaş altı olduğun için hesabın kapatılacaktır. Devam etmek istiyor musun?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            style: FilledButton.styleFrom(
              backgroundColor: ctx.kc.haksizFill,
              foregroundColor: ctx.kc.onColor,
            ),
            onPressed: () async {
              Navigator.pop(ctx);
              try {
                await ref
                    .read(notificationServiceProvider)
                    .deleteCurrentToken();
                await ref.read(authServiceProvider).deleteAccount();
                ref.read(currentUserProvider.notifier).state = null;
                if (context.mounted) {
                  context.go('/');
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(
                      content:
                          Text('Hesabın 18 yaş kuralı nedeniyle kapatıldı.'),
                    ),
                  );
                }
              } catch (_) {}
            },
            child: const Text('Evet, Kapat'),
          ),
        ],
      ),
    );
  }

  void _confirmDeleteAccount(
      BuildContext context, WidgetRef ref, AuthUser user) {
    final isPasswordAccount = user.authProvider != 'google';
    final passwordCtrl = TextEditingController();
    var isDeleting = false;
    String? errorText;

    showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setState) {
          Future<void> submit() async {
            if (isPasswordAccount && passwordCtrl.text.trim().isEmpty) {
              setState(() => errorText = 'Şifreni gir.');
              return;
            }
            setState(() {
              isDeleting = true;
              errorText = null;
            });
            try {
              await ref.read(notificationServiceProvider).deleteCurrentToken();
              await ref.read(authServiceProvider).deleteAccount(
                    password: isPasswordAccount ? passwordCtrl.text : null,
                  );
              ref.read(currentUserProvider.notifier).state = null;
              if (!ctx.mounted) return;
              Navigator.pop(ctx);
              if (!context.mounted) return;
              await showDialog<void>(
                context: context,
                barrierDismissible: false,
                builder: (dialogCtx) => PopScope(
                  canPop: false,
                  child: AlertDialog(
                    title: const Text('Hesabın kapatıldı'),
                    content: const Text(
                      'Oturumun kapatıldı. 30 gün içinde e-postana gönderilen link ile hesabını geri alabilirsin. Bu süre sonunda kimlik bilgilerin kalıcı olarak silinir; paylaştığın içerikler anonim olarak platformda kalır.',
                    ),
                    actions: [
                      FilledButton(
                        onPressed: () {
                          Navigator.pop(dialogCtx);
                          if (context.mounted) context.go('/auth/login');
                        },
                        child: const Text('Tamam'),
                      ),
                    ],
                  ),
                ),
              );
            } on ApiException catch (e) {
              if (!ctx.mounted) return;
              setState(() {
                isDeleting = false;
                errorText = e.friendlyMessage;
              });
            } catch (_) {
              if (!ctx.mounted) return;
              setState(() {
                isDeleting = false;
                errorText = 'Hesap silinemedi. Lütfen tekrar dene.';
              });
            }
          }

          return AlertDialog(
            title: const Text('Hesabı sil'),
            content: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'Bu işlem geri alınabilir (30 gün). Onaylarsan:\n'
                  '• Oturumun hemen kapatılır.\n'
                  '• Kullanıcı adın, e-posta ve şifren 30 gün sonra silinir.\n'
                  '• Paylaştığın içerikler anonim olarak kalır.',
                ),
                if (isPasswordAccount) ...[
                  const SizedBox(height: 16),
                  TextField(
                    controller: passwordCtrl,
                    enabled: !isDeleting,
                    obscureText: true,
                    autofocus: true,
                    decoration: InputDecoration(
                        labelText: 'Şifreni gir', errorText: errorText),
                    onSubmitted: (_) {
                      if (!isDeleting) submit();
                    },
                  ),
                ] else if (errorText != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 12),
                    child: Text(
                      errorText!,
                      style: TextStyle(color: ctx.kc.haksizText),
                    ),
                  ),
              ],
            ),
            actions: [
              TextButton(
                onPressed: isDeleting ? null : () => Navigator.pop(ctx),
                child: const Text('Vazgeç'),
              ),
              FilledButton.icon(
                onPressed: isDeleting ? null : submit,
                style: FilledButton.styleFrom(
                  backgroundColor: ctx.kc.haksizFill,
                  foregroundColor: ctx.kc.onColor,
                ),
                icon: isDeleting
                    ? SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(
                            strokeWidth: 2, color: ctx.kc.onColor))
                    : const Icon(Icons.delete_forever_rounded),
                label: const Text('Hesabı sil'),
              ),
            ],
          );
        },
      ),
    ).whenComplete(passwordCtrl.dispose);
  }
}

class _SectionHeader extends StatelessWidget {
  const _SectionHeader(this.title);
  final String title;

  @override
  Widget build(BuildContext context) => SettingsSectionHeader(title);
}

/// A settings row (see [SettingsRow]); keeps the original call shape.
class _SettingsTile extends StatelessWidget {
  const _SettingsTile({
    required this.icon,
    required this.title,
    this.subtitle,
    this.onTap,
    this.trailing,
    this.destructive = false,
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final VoidCallback? onTap;
  final Widget? trailing;
  final bool destructive;

  @override
  Widget build(BuildContext context) {
    return SettingsRow(
      icon: icon,
      title: title,
      subtitle: subtitle,
      onTap: onTap,
      trailing: trailing,
      destructive: destructive,
    );
  }
}

enum _PushAction { reduce, disable, cancel }

class _PendingEmailBanner extends StatelessWidget {
  const _PendingEmailBanner({
    required this.pendingEmail,
    required this.onVerify,
    required this.onCancel,
  });

  final String pendingEmail;
  final VoidCallback onVerify;
  final VoidCallback onCancel;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Container(
      margin: const EdgeInsets.fromLTRB(16, 0, 16, 12),
      decoration: BoxDecoration(
        color: kc.primaryContainer,
        borderRadius: KararRadius.all(KararRadius.tile),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 14, 12, 10),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(Icons.mark_email_unread_rounded,
                    size: 20, color: kc.onPrimaryContainer),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Bekleyen e-posta değişikliği',
                    style: AppTypography.bodyStrong.copyWith(
                      color: kc.onPrimaryContainer,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Text(
              pendingEmail,
              style: AppTypography.bodyS.copyWith(
                color: kc.onPrimaryContainer,
                fontWeight: FontWeight.w700,
              ),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
            const SizedBox(height: 2),
            Text(
              'Yeni adresine doğrulama kodu gönderdik.',
              style: AppTypography.bodyS.copyWith(
                color: kc.onPrimaryContainer,
              ),
            ),
            const SizedBox(height: 10),
            Row(
              children: [
                FilledButton(
                  onPressed: onVerify,
                  style: FilledButton.styleFrom(
                    minimumSize: const Size(64, 44),
                    padding: const EdgeInsets.symmetric(horizontal: 18),
                    textStyle: AppTypography.label,
                  ),
                  child: const Text('Kodu Doğrula'),
                ),
                const SizedBox(width: 8),
                TextButton(
                  onPressed: onCancel,
                  style: TextButton.styleFrom(
                    foregroundColor: kc.haksizText,
                  ),
                  child: const Text('İptal'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
