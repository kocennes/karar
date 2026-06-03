import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/auth/auth_service.dart';
import 'package:karar/core/providers.dart';
import 'package:karar/features/profile/edit_profile_screen.dart';

void main() {
  testWidgets('EditProfileScreen can open from route without extra user',
      (tester) async {
    const user = AuthUser(
      id: 'user-1',
      username: 'kararci',
      email: 'kararci@example.com',
      karma: 42,
      authProvider: 'password',
      bio: 'Kisa bio',
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          currentUserProvider.overrideWith((ref) => user),
        ],
        child: const MaterialApp(
          home: EditProfileScreen(),
        ),
      ),
    );

    expect(find.text('kararci'), findsOneWidget);
    expect(find.text('Kisa bio'), findsOneWidget);
    expect(find.byType(TextField), findsNWidgets(2));
  });
}
