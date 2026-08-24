#!/usr/bin/env bash
#
# Creates the small set of believable customers, orders and appointments that
# the user-manual screenshots are taken against.
#
# Everything is created through the application's own forms — checkout, the
# booking page, the data-request form — so the resulting rows are exactly what
# a real shop's data looks like. Inserting straight into the tables would risk
# illustrating states the application itself can never produce.
#
# Run against a freshly seeded development database, never a real one.
set -u

B="${VISIONCART_URL:-http://localhost:5217}"
STAFF_EMAIL="${VISIONCART_EMAIL:-admin@visioncart.local}"
# No default: see the note in capture.mjs.
STAFF_PASSWORD="${VISIONCART_PASSWORD:?set VISIONCART_PASSWORD to the staff password from appsettings.Development.json}"

token() { # jar, path
  curl -s -b "$1" -c "$1" "$B$2" |
    grep -oP 'name="__RequestVerificationToken"[^>]*value="\K[^"]+' | head -1
}

# --- guest orders -------------------------------------------------------------
# Each is a full journey: add a frame to the bag, type a prescription, check out.
place_order() { # name, email, phone, city, sphere_od, cyl_od, axis_od, sphere_os, lens
  local name="$1" email="$2" phone="$3" city="$4"
  local od_s="$5" od_c="$6" od_a="$7" os_s="$8" lens="$9"
  local jar; jar=$(mktemp)

  local slug
  slug=$(curl -s "$B/frames" | grep -oP 'href="/frames/\K[a-z0-9-]+' | head -1)
  local variant_id
  variant_id=$(curl -s "$B/frames/$slug" |
    grep -oP 'name="VariantId"[^>]*value="\K[^"]+' | head -1)
  [ -z "$variant_id" ] && { echo "  ! could not find a variant for $name"; rm -f "$jar"; return 1; }

  local t; t=$(token "$jar" "/frames/$slug")
  curl -s -b "$jar" -c "$jar" -o /dev/null -X POST "$B/cart/add" \
    --data-urlencode "VariantId=$variant_id" \
    --data-urlencode "Qty=1" \
    --data-urlencode "LensMode=prescription" \
    --data-urlencode "LensOptionCodes=type-single" \
    --data-urlencode "LensOptionCodes=$lens" \
    --data-urlencode "OdSphere=$od_s" \
    --data-urlencode "OdCylinder=$od_c" \
    --data-urlencode "OdAxis=$od_a" \
    --data-urlencode "OsSphere=$os_s" \
    --data-urlencode "PdMm=63" \
    --data-urlencode "__RequestVerificationToken=$t"

  t=$(token "$jar" "/checkout")
  curl -s -b "$jar" -c "$jar" -o /dev/null -X POST "$B/checkout" \
    --data-urlencode "Email=$email" \
    --data-urlencode "FullName=$name" \
    --data-urlencode "Phone=$phone" \
    --data-urlencode "Line1=House 14, Gulberg III" \
    --data-urlencode "City=$city" \
    --data-urlencode "State=Punjab" \
    --data-urlencode "PostalCode=54000" \
    --data-urlencode "Country=PK" \
    --data-urlencode "PaymentMethod=cod" \
    --data-urlencode "__RequestVerificationToken=$t"

  echo "  ordered: $name"
  rm -f "$jar"
}

echo "Placing customer orders"
place_order "Ayesha Malik"  "ayesha.malik@example.com"  "+92 300 4412876" "Lahore"     "-2.25" "-0.75" "175" "-2.00" "idx-160"
place_order "Bilal Khan"    "bilal.khan@example.com"    "+92 321 7788190" "Lahore"     "-1.50" "-0.50" "90"  "-1.75" "idx-150"
place_order "Sana Iqbal"    "sana.iqbal@example.com"    "+92 333 9015524" "Islamabad"  "+1.75" "0"     ""    "+1.50" "idx-167"
place_order "Daniyal Ahmed" "daniyal.ahmed@example.com" "+92 345 2237701" "Karachi"    "-4.00" "-1.25" "10"  "-3.75" "idx-174"

# --- a customer account, with an address and an appointment -------------------
echo "Creating a customer account"
jar=$(mktemp)
t=$(token "$jar" "/register")
curl -s -b "$jar" -c "$jar" -o /dev/null -X POST "$B/register" \
  --data-urlencode "Name=Ayesha Malik" \
  --data-urlencode "Email=ayesha.malik@example.com" \
  --data-urlencode "Password=Demo!Passw0rd" \
  --data-urlencode "ConfirmPassword=Demo!Passw0rd" \
  --data-urlencode "__RequestVerificationToken=$t"

t=$(token "$jar" "/account/addresses/new")
curl -s -b "$jar" -c "$jar" -o /dev/null -X POST "$B/account/addresses/save" \
  --data-urlencode "Label=Home" \
  --data-urlencode "FullName=Ayesha Malik" \
  --data-urlencode "Phone=+92 300 4412876" \
  --data-urlencode "Line1=House 14, Gulberg III" \
  --data-urlencode "City=Lahore" \
  --data-urlencode "State=Punjab" \
  --data-urlencode "PostalCode=54000" \
  --data-urlencode "Country=PK" \
  --data-urlencode "__RequestVerificationToken=$t"
echo "  saved an address"

for offset in 0 1; do
  slot=$(curl -s -b "$jar" "$B/account/appointments/book" |
         grep -oP 'name="startsAt" value="\K[^"]+' | sed -n "$((offset + 3))p")
  [ -z "$slot" ] && continue
  t=$(token "$jar" "/account/appointments/book")
  curl -s -b "$jar" -c "$jar" -o /dev/null -X POST "$B/account/appointments/book" \
    --data-urlencode "startsAt=$slot" \
    --data-urlencode "kind=$([ $offset -eq 0 ] && echo eye_test || echo fitting)" \
    --data-urlencode "__RequestVerificationToken=$t"
  echo "  booked: $slot"
done
rm -f "$jar"

# --- a data request waiting for staff ----------------------------------------
echo "Raising a data request"
jar=$(mktemp)
t=$(token "$jar" "/account/privacy/request")
curl -s -b "$jar" -c "$jar" -o /dev/null -X POST "$B/account/privacy/request" \
  --data-urlencode "Kind=correction" \
  --data-urlencode "Email=bilal.khan@example.com" \
  --data-urlencode "Message=My surname is spelt Khan, not Kahn." \
  --data-urlencode "__RequestVerificationToken=$t"
rm -f "$jar"
echo "  raised"

echo
echo "Demo data ready."
