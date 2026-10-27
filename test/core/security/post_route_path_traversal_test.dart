// Güvenlik regresyonu: `/posts/:id` derin bağlantısı üzerinden yol enjeksiyonu.
//
// Saldırı: `https://karar.app/posts/..%2Fauth%2F2fa%2Fdisable%3F` gibi bir
// bağlantı go_router'da `postId = '../auth/2fa/disable?'` olarak çözülür,
// PostDetailScreen açılışta `recordView(postId)` çağırır ve Dio
// `normalizePath()` ile isteği kimlik doğrulamalı
// `POST /api/v1/auth/2fa/disable` adresine taşır.
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:karar/core/api/api_client.dart';
import 'package:karar/core/api/api_endpoints.dart';
import 'package:karar/core/api/api_exception.dart';
import 'package:karar/core/app_services.dart';
import 'package:karar/core/router/app_router.dart';
import 'package:karar/features/feed/post_repository.dart';
import 'package:karar/features/post_detail/post_detail_screen.dart';
import 'package:karar/features/profile/other_profile_screen.dart';

class _FakeServices implements AppServices {
  @override
  dynamic noSuchMethod(Invocation invocation) => null;
}

class _CaptureAdapter implements HttpClientAdapter {
  final List<RequestOptions> requests = [];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options);
    return ResponseBody.fromString('', 204);
  }

  @override
  void close({bool force = false}) {}
}

const _validPostId = '3f2504e0-4f89-41d3-9a0c-0305e82c3301';

const _traversalPostLinks = [
  // Flutter web PathUrlStrategy: location.pathname ham (decode edilmeden) gelir.
  '/posts/..%2Fauth%2F2fa%2Fdisable%3F',
  // Android App Links: intent data.toString().
  'https://karar.app/posts/..%2Fauth%2F2fa%2Fdisable%3F',
  // iOS custom scheme: url.absoluteString.
  'karar:///posts/..%2Fauth%2F2fa%2Fdisable%3F',
  '/posts/..%2Fauth%2F2fa%2Fsetup%23',
  '/posts/%2E%2E%2Fauth%2F2fa%2Fbackup-codes%3F',
  '/posts/..%2Fcomments%2F11111111-1111-1111-1111-111111111111%2Fupvote%3F?ref=abc',
  '/posts/22222222-2222-2222-2222-222222222222%2Fsave%3F',
  '/posts/..',
];

/// Gerçek `buildRouter()` tablosunda [location]'ı eşleştirir ve eşleşen
/// GoRoute'un builder'ının üreteceği widget'ı döndürür (ekran mount edilmez,
/// dolayısıyla hiçbir ağ isteği atılmaz).
Widget _buildLeaf(BuildContext context, GoRouter router, String location) {
  final matches = router.configuration.findMatch(Uri.parse(location));
  expect(matches.isError, isFalse, reason: location);
  final leaf = matches.last;
  return leaf.route.builder!(
      context, leaf.buildState(router.configuration, matches));
}

Future<BuildContext> _context(WidgetTester tester) async {
  late BuildContext captured;
  await tester.pumpWidget(Builder(builder: (context) {
    captured = context;
    return const SizedBox.shrink();
  }));
  return captured;
}

void main() {
  group('router: /posts/:id ve /users/:username parametre doğrulaması', () {
    testWidgets(
        'yol enjeksiyonu içeren post bağlantıları PostDetailScreen açmaz',
        (tester) async {
      final context = await _context(tester);
      final router = buildRouter(_FakeServices());
      addTearDown(router.dispose);

      for (final link in _traversalPostLinks) {
        final widget = _buildLeaf(context, router, link);
        expect(
          widget,
          isNot(isA<PostDetailScreen>()),
          reason: '$link -> PostDetailScreen(postId: '
              '${widget is PostDetailScreen ? widget.postId : '-'})',
        );
      }
    });

    testWidgets(
        'giriş sonrası returnTo zinciri de geçersiz post kimliğini reddeder',
        (tester) async {
      final context = await _context(tester);
      final router = buildRouter(_FakeServices());
      addTearDown(router.dispose);

      final login = Uri.parse(
        '/auth/login?returnTo=%2Fposts%2F..%252Fauth%252F2fa%252Fdisable%253F',
      );
      final returnTo = login.queryParameters['returnTo']!;
      // _finishAuth -> context.go(returnTo)
      final widget = _buildLeaf(context, router, returnTo);
      expect(widget, isNot(isA<PostDetailScreen>()));
    });

    testWidgets(
        'geçerli UUID post bağlantısı PostDetailScreen açmaya devam eder',
        (tester) async {
      final context = await _context(tester);
      final router = buildRouter(_FakeServices());
      addTearDown(router.dispose);

      for (final link in [
        '/posts/$_validPostId',
        'https://karar.app/posts/$_validPostId?ref=share',
        '/posts/${_validPostId.toUpperCase()}?commentId=c1&source=notification',
      ]) {
        final widget = _buildLeaf(context, router, link);
        expect(widget, isA<PostDetailScreen>(), reason: link);
        expect(
          (widget as PostDetailScreen).postId.toLowerCase(),
          _validPostId,
        );
      }
    });

    testWidgets('profil bağlantıları kullanıcı adı biçimine uymalı',
        (tester) async {
      final context = await _context(tester);
      final router = buildRouter(_FakeServices());
      addTearDown(router.dispose);

      for (final link in [
        '/users/..%2Fauth%2F2fa%2Fdisable%3F',
        '/users/ali%2F..%2F..%2Fauth',
        '/users/..',
      ]) {
        final widget = _buildLeaf(context, router, link);
        expect(widget, isNot(isA<OtherProfileScreen>()), reason: link);
      }

      final ok = _buildLeaf(context, router, '/users/Ali_Veli_42');
      expect(ok, isA<OtherProfileScreen>());
      expect((ok as OtherProfileScreen).username, 'Ali_Veli_42');
    });
  });

  group('ApiClient: kimlik doğrulamalı istek başka uç noktaya kaçmaz', () {
    late _CaptureAdapter adapter;
    late ApiClient client;

    setUp(() {
      adapter = _CaptureAdapter();
      final dio = Dio(BaseOptions(baseUrl: 'https://api.example.test'))
        ..httpClientAdapter = adapter;
      client = ApiClient(
        baseUrl: 'https://api.example.test',
        dio: dio,
        accessTokenReader: () async => 'ACCESS',
        deviceTokenReader: () async => 'DEVICE',
      );
    });

    test('recordView zararlı postId ile /posts/{id}/view dışına istek atmaz',
        () async {
      final repo = PostRepository(apiClient: client);
      for (final id in [
        '../auth/2fa/disable?',
        '../auth/2fa/setup#',
        '../auth/2fa/backup-codes?',
        '%2E%2E/auth/2fa/disable?',
        '.%2e/auth/2fa/disable?',
        '../comments/11111111-1111-1111-1111-111111111111/upvote?',
        '22222222-2222-2222-2222-222222222222/save?',
        '../categories/3/follow?',
        '../notifications/clear-read?',
        '..',
        '.',
      ]) {
        await repo.recordView(id);
        await repo.recordView(id, dwellSeconds: 5, wasInteracted: false);
      }

      for (final request in adapter.requests) {
        expect(
          request.uri.path,
          matches(RegExp(r'^/api/v1/posts/[^/]+/view$')),
          reason: '${request.method} ${request.uri}',
        );
      }
      expect(
        adapter.requests.where((r) => r.uri.path.contains('/auth/')),
        isEmpty,
      );
    });

    test('geçerli postId ile görüntülenme kaydı normal gönderilir', () async {
      await PostRepository(apiClient: client).recordView(_validPostId);

      expect(adapter.requests, hasLength(1));
      expect(adapter.requests.single.method, 'POST');
      expect(
        adapter.requests.single.uri.toString(),
        'https://api.example.test/api/v1/posts/$_validPostId/view',
      );
    });

    test('nokta segmenti, ? veya # içeren yol hiç gönderilmeden reddedilir',
        () async {
      for (final path in [
        '/api/v1/posts/../auth/2fa/disable',
        '/api/v1/posts/%2E%2E/auth/2fa/disable',
        '/api/v1/posts/.%2e/auth/2fa/disable',
        '/api/v1/posts/./x',
        '/api/v1/posts/x?/view',
        '/api/v1/posts/x#/view',
        r'/api/v1/posts/..\auth/view',
        '/api/v1/posts/x\t/view',
        'https://evil.example/api/v1/posts',
      ]) {
        await expectLater(
          client.postJson<void>(path),
          throwsA(
            isA<ApiException>().having((e) => e.code, 'code', 'INVALID_PATH'),
          ),
          reason: path,
        );
        await expectLater(
          client.getJson<Object?>(path),
          throwsA(isA<ApiException>()),
          reason: path,
        );
        await expectLater(
          client.deleteJson<void>(path),
          throwsA(isA<ApiException>()),
          reason: path,
        );
        await expectLater(
          client.putJson<void>(path),
          throwsA(isA<ApiException>()),
          reason: path,
        );
        await expectLater(
          client.getBytes(path),
          throwsA(isA<ApiException>()),
          reason: path,
        );
        expect(await client.sseStream(path).toList(), isEmpty, reason: path);
      }
      expect(adapter.requests, isEmpty);
    });
  });

  group('ApiEndpoints: kimlikler tek yol segmenti olarak kodlanır', () {
    test('/, ?, # ve % kodlanır', () {
      expect(
        ApiEndpoints.postView('a/b?c#d%2F'),
        '/api/v1/posts/a%2Fb%3Fc%23d%252F/view',
      );
      expect(
        ApiEndpoints.userPosts('ali/../me'),
        '/api/v1/users/ali%2F..%2Fme/posts',
      );
      expect(ApiEndpoints.postView(_validPostId),
          '/api/v1/posts/$_validPostId/view');
    });

    test('. ve .. segmentleri reddedilir', () {
      expect(() => ApiEndpoints.postView('..'), throwsArgumentError);
      expect(() => ApiEndpoints.post('.'), throwsArgumentError);
      expect(() => ApiEndpoints.comment('..'), throwsArgumentError);
    });
  });
}
