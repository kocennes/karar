ALTER TABLE users
ADD COLUMN date_of_birth DATE,
ADD COLUMN gender TEXT CHECK (gender IN ('male', 'female', 'other', 'prefer_not_to_say'));
