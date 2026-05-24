import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../app_services.dart';
import '../auth/auth_service.dart';
import '../providers.dart';
import '../../features/auth/change_username_sheet.dart';
import '../../features/auth/login_screen.dart';
import '../../features/auth/register_screen.dart';
import '../../features/auth/sessions_screen.dart';
import '../../features/auth/two_factor_setup_screen.dart';
import '../../features/auth/verify_email_screen.dart';
import '../../features/create_post/create_post_screen.dart';
import '../../features/feed/category_screen.dart';
import '../../features/feed/feed_screen.dart';
import '../../features/feed/discover_screen.dart';
import '../../features/home/home_shell.dart';
import '../../features/home/splash_screen.dart';
import '../../features/legal/contact_screen.dart';
import '../../features/legal/copyright_complaint_screen.dart';
import '../../features/legal/legal_screen.dart';
import '../../features/legal/moderation_transparency_screen.dart';
import '../../features/notifications/notifications_screen.dart';
import '../../features/post_detail/post_detail_screen.dart';
import '../../features/profile/edit_profile_screen.dart';
import '../../features/profile/karma_history_screen.dart';
import '../../features/profile/weekly_stats_screen.dart';
import '../../features/profile/my_comments_screen.dart';
import '../../features/profile/my_posts_screen.dart';
import '../../features/profile/other_profile_screen.dart';
import '../../features/profile/profile_screen.dart';
import '../../features/profile/saved_posts_screen.dart';
import '../../features/search/search_screen.dart';
import '../../features/settings/blocked_users_screen.dart';
import '../../features/settings/moderation_history_screen.dart';
import '../../features/settings/settings_screen.dart';
import '../../features/settings/change_password_screen.dart';
import '../../features/settings/feedback_screen.dart';
import '../../features/auth/backup_codes_screen.dart';
import '../../features/auth/change_email_screen.dart';
import '../../features/auth/forgot_password_screen.dart';
import '../../features/onboarding/onboarding_screen.dart';
import '../../shared/models/post.dart';

GoRouter buildRouter(AppServices services) => GoRouter(
      initialLocation: '/splash',
      debugLogDiagnostics: false,
      routes: [
        GoRoute(
          path: '/splash',
          builder: (_, __) => const SplashScreen(),
        ),
        GoRoute(
          path: '/onboarding',
          builder: (_, __) => const OnboardingScreen(),
        ),
        // ── Shell — bottom nav ───────────────────────────────────────────────
        StatefulShellRoute.indexedStack(
          builder: (context, state, shell) => HomeShell(navigationShell: shell),
          branches: [
            // Feed tab
            StatefulShellBranch(
              routes: [
                GoRoute(
                  path: '/',
                  builder: (_, __) => const FeedScreen(),
                ),
              ],
            ),
            // Create tab
            StatefulShellBranch(
              routes: [
                GoRoute(
                  path: '/create',
                  builder: (_, __) => const CreatePostScreen(),
                ),
              ],
            ),
            // Notifications tab
            StatefulShellBranch(
              routes: [
                GoRoute(
                  path: '/notifications',
                  builder: (_, __) => const NotificationsScreen(),
                ),
              ],
            ),
            // Profile tab
            StatefulShellBranch(
              routes: [
                GoRoute(
                  path: '/profile',
                  builder: (_, __) => const ProfileScreen(),
                  routes: [
                    GoRoute(
                      path: 'posts',
                      builder: (_, __) => const MyPostsScreen(),
                    ),
                    GoRoute(
                      path: 'saved',
                      builder: (_, __) => const SavedPostsScreen(),
                    ),
                    GoRoute(
                      path: 'comments',
                      builder: (_, __) => const MyCommentsScreen(),
                    ),
                    GoRoute(
                      path: 'karma',
                      builder: (_, __) => const KarmaHistoryScreen(),
                    ),
                    GoRoute(
                      path: 'weekly',
                      builder: (_, __) => const WeeklyStatsScreen(),
                    ),
                    GoRoute(
                      path: 'edit',
                      builder: (_, state) => EditProfileScreen(
                        user: state.extra! as AuthUser,
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ],
        ),

        // ── Search ───────────────────────────────────────────────────────────
        GoRoute(
          path: '/search',
          builder: (_, __) => const SearchScreen(),
        ),

        // ── Discover ─────────────────────────────────────────────────────────
        GoRoute(
          path: '/discover',
          builder: (_, __) => const DiscoverScreen(),
        ),

        // ── Category feed (F11) ──────────────────────────────────────────────
        GoRoute(
          path: '/categories/:id',
          builder: (_, state) => CategoryScreen(
            categoryId: int.parse(state.pathParameters['id']!),
          ),
        ),

        // ── Settings ─────────────────────────────────────────────────────────
        GoRoute(
          path: '/settings',
          builder: (_, __) => const SettingsScreen(),
          routes: [
            GoRoute(
              path: 'change-password',
              builder: (_, __) => const ChangePasswordScreen(),
            ),
            GoRoute(
              path: '2fa-setup',
              builder: (_, __) => const TwoFactorSetupScreen(),
            ),
            GoRoute(
              path: 'sessions',
              builder: (_, __) => const SessionsScreen(),
            ),
            GoRoute(
              path: 'feedback',
              builder: (_, __) => const FeedbackScreen(),
            ),
            GoRoute(
              path: 'blocked-users',
              builder: (_, __) => const BlockedUsersScreen(),
            ),
            GoRoute(
              path: 'change-email',
              builder: (_, __) => const ChangeEmailScreen(),
            ),
            GoRoute(
              path: 'moderation-history',
              builder: (_, __) => const ModerationHistoryScreen(),
            ),
          ],
        ),

        // ── 2FA Yedek Kodlar ─────────────────────────────────────────────────
        GoRoute(
          path: '/2fa/backup-codes',
          builder: (_, state) => BackupCodesScreen(
            codes: (state.extra as List<String>?) ?? [],
          ),
        ),

        // ── Auth — Şifremi Unuttum ────────────────────────────────────────────
        GoRoute(
          path: '/auth/forgot-password',
          builder: (_, __) => const ForgotPasswordScreen(),
        ),

        // ── Legal ───────────────────────────────────────────────────────────
        GoRoute(
          path: '/legal/terms',
          builder: (_, __) => const LegalScreen(
            title: 'Kullanım Koşulları',
            assetPath: 'docs/legal.md',
          ),
        ),
        GoRoute(
          path: '/legal/privacy',
          builder: (_, __) => const LegalScreen(
            title: 'Gizlilik Politikası',
            assetPath: 'docs/privacy.md',
          ),
        ),
        GoRoute(
          path: '/legal/community',
          builder: (_, __) => const LegalScreen(
            title: 'Topluluk Kuralları',
            assetPath: 'docs/community-guidelines.md',
          ),
        ),
        GoRoute(
          path: '/legal/content-policy',
          builder: (_, __) => const LegalScreen(
            title: 'İçerik Politikası',
            assetPath: 'docs/content-policy.md',
          ),
        ),
        GoRoute(
          path: '/legal/contact',
          builder: (_, __) => const ContactScreen(),
        ),
        GoRoute(
          path: '/legal/copyright',
          builder: (_, __) => const CopyrightComplaintScreen(),
        ),
        GoRoute(
          path: '/legal/moderation-transparency',
          builder: (_, __) => const ModerationTransparencyScreen(),
        ),

        // ── Post detail (top-level so any tab can navigate here) ─────────────
        GoRoute(
          path: '/posts/:id',
          builder: (_, state) {
            final commentId = state.uri.queryParameters['commentId'];
            return PostDetailScreen(
              postId: state.pathParameters['id']!,
              post: state.extra as Post?,
              initialCommentId: commentId,
            );
          },
        ),

        // ── Profiles ────────────────────────────────────────────────────────
        GoRoute(
          path: '/users/:username',
          builder: (_, state) => OtherProfileScreen(
            username: state.pathParameters['username']!,
          ),
        ),

        // ── Auth ─────────────────────────────────────────────────────────────
        GoRoute(
          path: '/auth/login',
          builder: (_, state) => _LoginWrapper(
            authService: services.authService,
            returnTo: state.uri.queryParameters['returnTo'],
          ),
        ),
        GoRoute(
          path: '/auth/register',
          builder: (_, state) => _RegisterWrapper(
            authService: services.authService,
            returnTo: state.uri.queryParameters['returnTo'],
          ),
        ),
        GoRoute(
          path: '/auth/verify-email',
          builder: (_, state) => _VerifyEmailWrapper(
            authService: services.authService,
            email: state.extra as String,
            returnTo: state.uri.queryParameters['returnTo'],
          ),
        ),
      ],
    );

// ── Route wrappers that update currentUserProvider ────────────────────────────

class _LoginWrapper extends ConsumerWidget {
  const _LoginWrapper({required this.authService, this.returnTo});
  final AuthService authService;
  final String? returnTo;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return LoginScreen(
      authService: authService,
      onSuccess: () async {
        // G06: migrate any guest data silently (errors are non-fatal)
        authService.migrateGuestData().catchError((_) {});

        final user = authService.currentUser;
        if (user != null && user.isNewUser) {
          await ChangeUsernameSheet.show(
            context,
            authService: authService,
            onSuccess: () {
              ref.read(currentUserProvider.notifier).state =
                  authService.currentUser;
            },
          );
        }
        if (context.mounted) {
          ref.read(currentUserProvider.notifier).state =
              authService.currentUser;
          _finishAuth(context, returnTo);
        }
      },
    );
  }
}

class _RegisterWrapper extends ConsumerStatefulWidget {
  const _RegisterWrapper({required this.authService, this.returnTo});
  final AuthService authService;
  final String? returnTo;

  @override
  ConsumerState<_RegisterWrapper> createState() => _RegisterWrapperState();
}

class _RegisterWrapperState extends ConsumerState<_RegisterWrapper> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(analyticsServiceProvider).logRegisterStarted();
    });
  }

  @override
  Widget build(BuildContext context) {
    return RegisterScreen(
      authService: widget.authService,
      returnTo: widget.returnTo,
      onSuccess: () async {
        // G06: migrate any guest data silently (errors are non-fatal)
        widget.authService.migrateGuestData().catchError((_) {});

        final user = widget.authService.currentUser;
        final isNewUser = user?.isNewUser == true;
        if (user != null && isNewUser) {
          await ChangeUsernameSheet.show(
            context,
            authService: widget.authService,
            onSuccess: () {
              ref.read(currentUserProvider.notifier).state =
                  widget.authService.currentUser;
            },
          );
        }
        if (context.mounted) {
          if (isNewUser) {
            ref
                .read(analyticsServiceProvider)
                .logRegisterCompleted(method: 'google');
          }
          ref.read(currentUserProvider.notifier).state =
              widget.authService.currentUser;
          _finishAuth(context, widget.returnTo);
        }
      },
    );
  }
}

class _VerifyEmailWrapper extends ConsumerWidget {
  const _VerifyEmailWrapper({
    required this.authService,
    required this.email,
    this.returnTo,
  });
  final AuthService authService;
  final String email;
  final String? returnTo;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return VerifyEmailScreen(
      authService: authService,
      email: email,
      onSuccess: () async {
        // G06: migrate any guest data silently (errors are non-fatal)
        authService.migrateGuestData().catchError((_) {});

        final user = authService.currentUser;
        if (user != null && user.isNewUser) {
          await ChangeUsernameSheet.show(
            context,
            authService: authService,
            onSuccess: () {
              ref.read(currentUserProvider.notifier).state =
                  authService.currentUser;
            },
          );
        }
        if (context.mounted) {
          ref
              .read(analyticsServiceProvider)
              .logRegisterCompleted(method: 'email');
          ref.read(currentUserProvider.notifier).state =
              authService.currentUser;
          _finishAuth(context, returnTo);
        }
      },
    );
  }
}

void _finishAuth(BuildContext context, String? returnTo) {
  if (returnTo != null &&
      returnTo.isNotEmpty &&
      !returnTo.startsWith('/auth/')) {
    context.go(returnTo);
    return;
  }
  if (context.canPop()) {
    context.pop();
  } else {
    context.go('/');
  }
}
